using System.Net;
using System.Net.Http.Json;
using FuelFlow.Application.Common;
using FuelFlow.Application.Dispatch;
using FuelFlow.Application.Orders;
using FuelFlow.Domain.Orders;
using FuelFlow.Domain.Users;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace FuelFlow.Api.IntegrationTests;

public class DispatchApiTests(FuelFlowApiFactory factory) : ApiTestBase(factory)
{
    /// <summary>An order that is out for delivery: placed, confirmed, scheduled by the scheduler, then started.</summary>
    private async Task<(OrderDto Order, HttpClient Dispatcher, Session Customer)> OutForDeliveryOrderAsync(int litres = 1000)
    {
        var customer = await RegisterCustomerAsync();
        var dispatcher = await StaffClientAsync(UserRole.Dispatcher);
        var productId = await SeedProductAsync(pricePerLitre: 10m);
        var address = await CreateAddressAsync(customer.Client);
        await SeedWindowAsync(1);
        var order = await PlaceOrderAsync(customer, productId, address.Id, litres);
        await ConfirmAsync(dispatcher, order.Id);
        await RunSchedulerAsync();
        var started = await Read<OrderDto>(await dispatcher.PostAsync($"/api/dispatch/orders/{order.Id}/out-for-delivery", null, Ct));
        return (started, dispatcher, customer);
    }

    [Fact]
    public async Task DispatchEndpoints_CustomerAnonymousDispatcherAdmin_Return403401And200()
    {
        var customer = await RegisterCustomerAsync();
        var dispatcher = await StaffClientAsync(UserRole.Dispatcher, "dispatcher@example.com");
        var admin = await StaffClientAsync(UserRole.Admin, "admin@example.com");
        using var anonymous = Factory.CreateClient();

        (await customer.Client.GetAsync("/api/dispatch/orders", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await anonymous.GetAsync("/api/dispatch/orders", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await dispatcher.GetAsync("/api/dispatch/orders", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await admin.GetAsync("/api/dispatch/orders", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await customer.Client.PostAsJsonAsync("/api/dispatch/delivery-windows", new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Regions_Dispatcher_ListsConfiguredRegionsAndCustomerIsRejected()
    {
        var dispatcher = await StaffClientAsync(UserRole.Dispatcher);
        var customer = await RegisterCustomerAsync();

        var regions = await Read<List<RegionDto>>(await dispatcher.GetAsync("/api/dispatch/regions", Ct));

        regions.Select(r => r.Name).ShouldBe(["Copenhagen", "Zealand", "Funen", "South Jutland", "Aarhus", "North Jutland"]);
        regions[0].ShouldBe(new RegionDto("Copenhagen", 1000, 2999));
        (await customer.Client.GetAsync("/api/dispatch/regions", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListOrders_ShowsEveryCustomersOrdersWithContactAddressAndRegion()
    {
        var anna = await RegisterCustomerAsync("anna@example.com");
        var jensen = await RegisterCustomerAsync("kontor@jensen.example", business: true);
        var dispatcher = await StaffClientAsync(UserRole.Dispatcher);
        var productId = await SeedProductAsync();
        var annasAddress = await CreateAddressAsync(anna.Client, postalCode: "2100");
        var jensensAddress = await CreateAddressAsync(jensen.Client, postalCode: "8000");
        await PlaceOrderAsync(anna, productId, annasAddress.Id);
        await PlaceOrderAsync(jensen, productId, jensensAddress.Id);

        var all = await Read<PagedResult<DispatchOrderDto>>(await dispatcher.GetAsync("/api/dispatch/orders", Ct));

        all.TotalCount.ShouldBe(2);
        var byName = all.Items.ToDictionary(o => o.CustomerName);
        byName["Anna Hansen"].Region.ShouldBe("Copenhagen");
        byName["Anna Hansen"].Address.PostalCode.ShouldBe("2100");
        byName["Jensen ApS"].Region.ShouldBe("Aarhus");
        byName["Jensen ApS"].CustomerPhone.ShouldBe("+4587654321");
        byName["Jensen ApS"].DeliveryDate.ShouldBeNull();
    }

    [Fact]
    public async Task ListOrders_Filters_ByStatusRegionAndDeliveryDate()
    {
        var anna = await RegisterCustomerAsync("anna@example.com");
        var jensen = await RegisterCustomerAsync("kontor@jensen.example", business: true);
        var dispatcher = await StaffClientAsync(UserRole.Dispatcher);
        var productId = await SeedProductAsync();
        var annasAddress = await CreateAddressAsync(anna.Client, postalCode: "2100");
        var jensensAddress = await CreateAddressAsync(jensen.Client, postalCode: "8000");
        await SeedWindowAsync(1, "Copenhagen");
        await SeedWindowAsync(2, "Aarhus");
        var annasOrder = await PlaceOrderAsync(anna, productId, annasAddress.Id);
        var jensensOrder = await PlaceOrderAsync(jensen, productId, jensensAddress.Id);
        await ConfirmAsync(dispatcher, annasOrder.Id);
        await ConfirmAsync(dispatcher, jensensOrder.Id);
        await RunSchedulerAsync();

        async Task<List<Guid>> Ids(string query) =>
            (await Read<PagedResult<DispatchOrderDto>>(await dispatcher.GetAsync($"/api/dispatch/orders?{query}", Ct)))
                .Items.Select(o => o.Order.Id).ToList();

        (await Ids("status=Scheduled")).ShouldBe([annasOrder.Id, jensensOrder.Id], ignoreOrder: true);
        (await Ids("status=Pending")).ShouldBeEmpty();
        (await Ids("region=Copenhagen")).ShouldBe([annasOrder.Id]);
        (await Ids("region=aarhus")).ShouldBe([jensensOrder.Id]);
        (await Ids($"date={Day(1):yyyy-MM-dd}")).ShouldBe([annasOrder.Id]);
        (await Ids($"date={Day(2):yyyy-MM-dd}&region=Aarhus&status=Scheduled")).ShouldBe([jensensOrder.Id]);
        (await Ids($"date={Day(2):yyyy-MM-dd}&region=Copenhagen")).ShouldBeEmpty();
    }

    [Fact]
    public async Task ListOrders_Paging_IsAppliedAndInvalidInputIsRejected()
    {
        var anna = await RegisterCustomerAsync();
        var dispatcher = await StaffClientAsync(UserRole.Dispatcher);
        var productId = await SeedProductAsync();
        var address = await CreateAddressAsync(anna.Client);
        for (var i = 0; i < 3; i++)
        {
            Factory.Time.Advance(TimeSpan.FromMinutes(1));
            await PlaceOrderAsync(anna, productId, address.Id, 600 + i);
        }

        var page2 = await Read<PagedResult<DispatchOrderDto>>(await dispatcher.GetAsync("/api/dispatch/orders?page=2&pageSize=2", Ct));
        var unknownRegion = await dispatcher.GetAsync("/api/dispatch/orders?region=Atlantis", Ct);
        var tooBig = await dispatcher.GetAsync("/api/dispatch/orders?pageSize=101", Ct);

        (page2.TotalCount, page2.Items.Count).ShouldBe((3, 1));
        unknownRegion.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await unknownRegion.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(Ct))!.Errors.Keys.ShouldContain("region");
        tooBig.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Confirm_PendingOrder_ConfirmsItAndASecondConfirmReturns409ProblemDetails()
    {
        var anna = await RegisterCustomerAsync();
        var dispatcher = await StaffClientAsync(UserRole.Dispatcher);
        var order = await PlaceOrderAsync(anna, await SeedProductAsync(), (await CreateAddressAsync(anna.Client)).Id);
        Factory.Time.Advance(TimeSpan.FromMinutes(10));

        var confirmed = await ConfirmAsync(dispatcher, order.Id);
        var again = await dispatcher.PostAsync($"/api/dispatch/orders/{order.Id}/confirm", null, Ct);

        confirmed.Status.ShouldBe(OrderStatus.Confirmed);
        confirmed.ConfirmedAt.ShouldBe(FuelFlowApiFactory.StartTime.AddMinutes(10));
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        again.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Theory]
    [InlineData("out-for-delivery")] // not scheduled yet
    [InlineData("deliver")]
    public async Task Transition_OutOfOrder_Returns409AndLeavesStatusUnchanged(string action)
    {
        var anna = await RegisterCustomerAsync();
        var dispatcher = await StaffClientAsync(UserRole.Dispatcher);
        var order = await PlaceOrderAsync(anna, await SeedProductAsync(), (await CreateAddressAsync(anna.Client)).Id);
        await ConfirmAsync(dispatcher, order.Id);

        var response = await dispatcher.PostAsJsonAsync($"/api/dispatch/orders/{order.Id}/{action}", new { deliveredLitres = 1000 }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await LoadOrderAsync(order.Id)).Status.ShouldBe(OrderStatus.Confirmed);
    }

    [Fact]
    public async Task Dispatch_UnknownOrder_Returns404()
    {
        var dispatcher = await StaffClientAsync(UserRole.Dispatcher);

        (await dispatcher.PostAsync($"/api/dispatch/orders/{Guid.NewGuid()}/confirm", null, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(899)]
    [InlineData(1101)]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Deliver_LitresOutsideTenPercentOrNotPositive_Returns400AndOrderStaysOutForDelivery(int litres)
    {
        var (order, dispatcher, _) = await OutForDeliveryOrderAsync(1000);

        var response = await dispatcher.PostAsJsonAsync($"/api/dispatch/orders/{order.Id}/deliver", new { deliveredLitres = litres }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await LoadOrderAsync(order.Id)).Status.ShouldBe(OrderStatus.OutForDelivery);
    }

    [Fact]
    public async Task Deliver_MoreThanTenPercentUnderWithAReason_SucceedsAndTheCustomerSeesTheReason()
    {
        var (order, dispatcher, customer) = await OutForDeliveryOrderAsync(1000);

        var response = await dispatcher.PostAsJsonAsync(
            $"/api/dispatch/orders/{order.Id}/deliver", new { deliveredLitres = 600, reason = "truck was out of product" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var seenByCustomer = await Read<OrderDto>(await customer.Client.GetAsync($"/api/orders/{order.Id}", Ct));
        seenByCustomer.DeliveredLitres.ShouldBe(600);
        seenByCustomer.ShortDeliveryReason.ShouldBe("truck was out of product");
    }

    [Fact]
    public async Task Deliver_MoreThanTenPercentOverEvenWithAReason_Returns400()
    {
        var (order, dispatcher, _) = await OutForDeliveryOrderAsync(1000);

        var response = await dispatcher.PostAsJsonAsync(
            $"/api/dispatch/orders/{order.Id}/deliver", new { deliveredLitres = 1101, reason = "extra" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await LoadOrderAsync(order.Id)).Status.ShouldBe(OrderStatus.OutForDelivery);
    }

    [Fact]
    public async Task FullHappyPath_PlaceConfirmScheduleOutForDeliveryDeliver_CustomerSeesRecalculatedTotals()
    {
        var customer = await RegisterCustomerAsync();
        var dispatcher = await StaffClientAsync(UserRole.Dispatcher);
        var productId = await SeedProductAsync(pricePerLitre: 10m);
        var address = await CreateAddressAsync(customer.Client);
        var windowId = await SeedWindowAsync(1);

        var placed = await PlaceOrderAsync(customer, productId, address.Id, 1000);
        await ConfirmAsync(dispatcher, placed.Id);
        (await RunSchedulerAsync()).Scheduled.ShouldBe(1);
        (await LoadOrderAsync(placed.Id)).DeliveryWindowId.ShouldBe(windowId);
        await dispatcher.PostAsync($"/api/dispatch/orders/{placed.Id}/out-for-delivery", null, Ct);
        Factory.Time.Advance(TimeSpan.FromMinutes(30)); // stays within the token's lifetime
        var delivered = await Read<OrderDto>(await dispatcher.PostAsJsonAsync($"/api/dispatch/orders/{placed.Id}/deliver", new { deliveredLitres = 950 }, Ct));

        delivered.Status.ShouldBe(OrderStatus.Delivered);
        var seenByCustomer = await Read<OrderDto>(await customer.Client.GetAsync($"/api/orders/{placed.Id}", Ct));
        seenByCustomer.DeliveredLitres.ShouldBe(950);
        seenByCustomer.Price.TotalIncVat.ShouldBe(12_500m, "the price snapshot from order time never changes");
        seenByCustomer.FinalPrice!.NetAmount.ShouldBe(9_500m);
        seenByCustomer.FinalPrice.VatAmount.ShouldBe(2_375m);
        seenByCustomer.FinalPrice.TotalIncVat.ShouldBe(11_875m);
        seenByCustomer.DeliveredAt.ShouldBe(FuelFlowApiFactory.StartTime.AddMinutes(30));
    }

    [Fact]
    public async Task CreateWindow_ValidRequest_Returns201WithCanonicalRegionName()
    {
        var dispatcher = await StaffClientAsync(UserRole.Dispatcher);

        var response = await dispatcher.PostAsJsonAsync("/api/dispatch/delivery-windows",
            new { date = Day(3), region = "copenhagen", capacityLitres = 8000 }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var window = (await response.Content.ReadFromJsonAsync<DeliveryWindowDto>(Json, Ct))!;
        (window.Region, window.CapacityLitres, window.BookedLitres, window.RemainingLitres).ShouldBe(("Copenhagen", 8000, 0, 8000));
    }

    [Fact]
    public async Task CreateWindow_SameDateAndRegionTwice_Returns409()
    {
        var dispatcher = await StaffClientAsync(UserRole.Dispatcher);
        var body = new { date = Day(3), region = "Copenhagen", capacityLitres = 8000 };
        (await dispatcher.PostAsJsonAsync("/api/dispatch/delivery-windows", body, Ct)).EnsureSuccessStatusCode();

        var again = await dispatcher.PostAsJsonAsync("/api/dispatch/delivery-windows", body, Ct);

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        again.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task CreateWindow_InvalidInput_Returns400ListingFieldErrors()
    {
        var dispatcher = await StaffClientAsync(UserRole.Dispatcher);

        var response = await dispatcher.PostAsJsonAsync("/api/dispatch/delivery-windows",
            new { date = Day(-1), region = "Atlantis", capacityLitres = 0 }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = (await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(Ct))!;
        problem.Errors.Keys.ShouldBe(["date", "region", "capacityLitres"], ignoreOrder: true);
    }

    [Fact]
    public async Task ListWindows_DefaultsToTodayOnwardsAndFiltersByRangeAndRegion()
    {
        var dispatcher = await StaffClientAsync(UserRole.Dispatcher);
        await SeedWindowAsync(-1);                 // yesterday: hidden by default
        await SeedWindowAsync(1, "Copenhagen");
        await SeedWindowAsync(2, "Aarhus");
        await SeedWindowAsync(5, "Copenhagen");

        async Task<List<(DateOnly, string)>> Windows(string query) =>
            (await Read<List<DeliveryWindowDto>>(await dispatcher.GetAsync($"/api/dispatch/delivery-windows?{query}", Ct)))
                .Select(w => (w.Date, w.Region)).ToList();

        (await Windows("")).ShouldBe([(Day(1), "Copenhagen"), (Day(2), "Aarhus"), (Day(5), "Copenhagen")]);
        (await Windows("region=Copenhagen")).ShouldBe([(Day(1), "Copenhagen"), (Day(5), "Copenhagen")]);
        (await Windows($"from={Day(2):yyyy-MM-dd}&to={Day(4):yyyy-MM-dd}")).ShouldBe([(Day(2), "Aarhus")]);
        (await Windows($"from={Day(-1):yyyy-MM-dd}")).Count.ShouldBe(4);
    }
}

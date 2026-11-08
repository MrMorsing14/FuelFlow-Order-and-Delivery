using System.Net;
using System.Net.Http.Json;
using FuelFlow.Application.Common;
using FuelFlow.Application.Orders;
using FuelFlow.Domain.Orders;
using FuelFlow.Domain.Pricing;
using FuelFlow.Domain.Products;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace FuelFlow.Api.IntegrationTests;

public class OrderApiTests(FuelFlowApiFactory factory) : ApiTestBase(factory)
{
    private static object OrderBody(Guid productId, Guid addressId, int litres) =>
        new { productId, deliveryAddressId = addressId, litres };

    [Fact]
    public async Task Quote_PrivateCustomer_ReturnsBreakdownIncludingVatAndCreatesNoOrder()
    {
        var anna = await RegisterCustomerAsync();
        var productId = await SeedProductAsync(pricePerLitre: 10m);
        var address = await CreateAddressAsync(anna.Client);

        var quote = await Read<PriceBreakdown>(await anna.Client.PostAsJsonAsync("/api/quotes", OrderBody(productId, address.Id, 1000), Ct));

        quote.TotalExVat.ShouldBe(10_000m);
        quote.TotalIncVat.ShouldBe(12_500m);
        quote.DisplayIncVat.ShouldBeTrue();
        await using var scope = NewScope();
        (await Db(scope).Orders.CountAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Mgo_PrivateCustomer_QuoteAndOrderAreRefusedWith400()
    {
        var anna = await RegisterCustomerAsync();
        var mgoId = await SeedProductAsync(ProductType.Mgo);
        var address = await CreateAddressAsync(anna.Client, tankCapacityLitres: 200_000);

        var quote = await anna.Client.PostAsJsonAsync("/api/quotes", OrderBody(mgoId, address.Id, 20_000), Ct);
        var order = await anna.Client.PostAsJsonAsync("/api/orders", OrderBody(mgoId, address.Id, 20_000), Ct);

        quote.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        order.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        order.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await order.Content.ReadAsStringAsync(Ct)).ShouldContain("business customers");
        await using var scope = NewScope();
        (await Db(scope).Orders.CountAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Mgo_BusinessCustomer_CanOrderOneTruckAndGetsTheExplanationForAnAwkwardAmount()
    {
        var business = await RegisterCustomerAsync("kontor@jensen.example", business: true);
        var mgoId = await SeedProductAsync(ProductType.Mgo, pricePerLitre: 10m);
        var address = await CreateAddressAsync(business.Client, tankCapacityLitres: 200_000);

        var order = await PlaceOrderAsync(business, mgoId, address.Id, 42_000);
        var tooMuchForOneTruck = await business.Client.PostAsJsonAsync("/api/quotes", OrderBody(mgoId, address.Id, 45_000), Ct);

        order.OrderedLitres.ShouldBe(42_000);
        tooMuchForOneTruck.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await tooMuchForOneTruck.Content.ReadAsStringAsync(Ct)).ShouldContain("52.000 litres or more");
    }

    [Fact]
    public async Task Products_ListMarksMgoAsBusinessOnly()
    {
        await SeedProductAsync(ProductType.Mgo);
        await SeedProductAsync(ProductType.Diesel);

        var products = await Read<List<FuelFlow.Application.Products.ProductDto>>(await Factory.CreateClient().GetAsync("/api/products", Ct));

        products.Single(p => p.Type == ProductType.Mgo).RequiresBusinessCustomer.ShouldBeTrue();
        products.Single(p => p.Type == ProductType.Diesel).RequiresBusinessCustomer.ShouldBeFalse();
    }

    [Fact]
    public async Task Quote_BusinessCustomer5000Litres_AppliesDiscountAndShowsExVat()
    {
        var business = await RegisterCustomerAsync("kontor@jensen.example", business: true);
        var productId = await SeedProductAsync(pricePerLitre: 10m);
        var address = await CreateAddressAsync(business.Client);

        var quote = await Read<PriceBreakdown>(await business.Client.PostAsJsonAsync("/api/quotes", OrderBody(productId, address.Id, 5000), Ct));

        quote.DiscountAmount.ShouldBe(1000m);
        quote.TotalExVat.ShouldBe(49_000m);
        quote.DisplayIncVat.ShouldBeFalse();
    }

    [Theory]
    [InlineData(499)]
    [InlineData(3001)] // tank holds 3000
    public async Task QuoteAndPlace_QuantityBreaksARule_Returns400ProblemDetails(int litres)
    {
        var anna = await RegisterCustomerAsync();
        var productId = await SeedProductAsync();
        var address = await CreateAddressAsync(anna.Client, tankCapacityLitres: 3000);

        var quote = await anna.Client.PostAsJsonAsync("/api/quotes", OrderBody(productId, address.Id, litres), Ct);
        var order = await anna.Client.PostAsJsonAsync("/api/orders", OrderBody(productId, address.Id, litres), Ct);

        quote.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        order.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        order.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Place_ValidOrder_Returns201WithLocationAndPriceSnapshot()
    {
        var anna = await RegisterCustomerAsync();
        var productId = await SeedProductAsync(pricePerLitre: 10m);
        var address = await CreateAddressAsync(anna.Client);

        var response = await anna.Client.PostAsJsonAsync("/api/orders", OrderBody(productId, address.Id, 500), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var order = (await response.Content.ReadFromJsonAsync<OrderDto>(Json, Ct))!;
        response.Headers.Location!.AbsolutePath.ShouldBe($"/api/orders/{order.Id}");
        order.Status.ShouldBe(OrderStatus.Pending);
        order.CreatedAt.ShouldBe(FuelFlowApiFactory.StartTime);
        order.Price.SmallOrderFee.ShouldBe(250m);
        order.Price.TotalIncVat.ShouldBe(6_562.50m);
        order.FinalPrice.ShouldBeNull();
    }

    [Fact]
    public async Task Place_UnknownProductOrAnotherCustomersAddress_Returns404()
    {
        var anna = await RegisterCustomerAsync("anna@example.com");
        var ben = await RegisterCustomerAsync("ben@example.com");
        var productId = await SeedProductAsync();
        var annasAddress = await CreateAddressAsync(anna.Client);
        var bensAddress = await CreateAddressAsync(ben.Client);

        (await anna.Client.PostAsJsonAsync("/api/orders", OrderBody(Guid.NewGuid(), annasAddress.Id, 1000), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anna.Client.PostAsJsonAsync("/api/orders", OrderBody(productId, bensAddress.Id, 1000), Ct))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Place_MissingFields_Returns400ListingFieldErrors()
    {
        var anna = await RegisterCustomerAsync();

        var response = await anna.Client.PostAsJsonAsync("/api/orders", new { litres = 0 }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = (await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(Ct))!;
        problem.Errors.Keys.ShouldBe(["productId", "deliveryAddressId", "litres"], ignoreOrder: true);
    }

    [Fact]
    public async Task List_OwnOrders_ArePagedNewestFirstWithTotalCountAndExcludeOthers()
    {
        var anna = await RegisterCustomerAsync("anna@example.com");
        var ben = await RegisterCustomerAsync("ben@example.com");
        var productId = await SeedProductAsync();
        var annasAddress = await CreateAddressAsync(anna.Client);
        var bensAddress = await CreateAddressAsync(ben.Client);
        var ids = new List<Guid>();
        foreach (var litres in new[] { 600, 700, 800 })
        {
            Factory.Time.Advance(TimeSpan.FromMinutes(1));
            ids.Add((await Read<OrderDto>(await anna.Client.PostAsJsonAsync("/api/orders", OrderBody(productId, annasAddress.Id, litres), Ct))).Id);
        }

        (await ben.Client.PostAsJsonAsync("/api/orders", OrderBody(productId, bensAddress.Id, 900), Ct)).EnsureSuccessStatusCode();

        var page1 = await Read<PagedResult<OrderDto>>(await anna.Client.GetAsync("/api/orders?page=1&pageSize=2", Ct));
        var page2 = await Read<PagedResult<OrderDto>>(await anna.Client.GetAsync("/api/orders?page=2&pageSize=2", Ct));

        page1.TotalCount.ShouldBe(3);
        page1.Items.Select(o => o.Id).ShouldBe([ids[2], ids[1]]);
        page2.Items.Select(o => o.Id).ShouldBe([ids[0]]);
    }

    [Theory]
    [InlineData("page=0", "page")]
    [InlineData("pageSize=101", "pageSize")]
    [InlineData("pageSize=0", "pageSize")]
    [InlineData("page=2147483647&pageSize=100", "page")]
    public async Task List_InvalidPaging_Returns400NamingTheField(string query, string field)
    {
        var anna = await RegisterCustomerAsync();

        var response = await anna.Client.GetAsync($"/api/orders?{query}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(Ct))!.Errors.Keys.ShouldContain(field);
    }

    [Fact]
    public async Task List_NoPagingParameters_UsesDefaultsAndMaxPageSizeIsAccepted()
    {
        var anna = await RegisterCustomerAsync();

        var defaults = await Read<PagedResult<OrderDto>>(await anna.Client.GetAsync("/api/orders", Ct));
        var max = await anna.Client.GetAsync("/api/orders?pageSize=100", Ct);

        (defaults.Page, defaults.PageSize, defaults.TotalCount).ShouldBe((1, 20, 0));
        max.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetAndCancel_AnotherCustomersOrder_Return404NotForbiddenAndLeaveOrderUntouched()
    {
        var anna = await RegisterCustomerAsync("anna@example.com");
        var ben = await RegisterCustomerAsync("ben@example.com");
        var productId = await SeedProductAsync();
        var address = await CreateAddressAsync(anna.Client);
        var order = await Read<OrderDto>(await anna.Client.PostAsJsonAsync("/api/orders", OrderBody(productId, address.Id, 1000), Ct));

        var get = await ben.Client.GetAsync($"/api/orders/{order.Id}", Ct);
        var cancel = await ben.Client.PostAsJsonAsync($"/api/orders/{order.Id}/cancel", new { reason = "mine now" }, Ct);
        var missing = await ben.Client.GetAsync($"/api/orders/{Guid.NewGuid()}", Ct);

        get.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        cancel.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await get.Content.ReadAsStringAsync(Ct)).ShouldNotContain(order.Id.ToString());
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await Read<OrderDto>(await anna.Client.GetAsync($"/api/orders/{order.Id}", Ct))).Status.ShouldBe(OrderStatus.Pending);
    }

    [Fact]
    public async Task Cancel_OwnPendingOrder_SetsCancelledWithReasonAndSecondCancelReturns409()
    {
        var anna = await RegisterCustomerAsync();
        var productId = await SeedProductAsync();
        var address = await CreateAddressAsync(anna.Client);
        var order = await Read<OrderDto>(await anna.Client.PostAsJsonAsync("/api/orders", OrderBody(productId, address.Id, 1000), Ct));
        Factory.Time.Advance(TimeSpan.FromMinutes(30)); // less than the 60-minute token lifetime

        var cancelled = await Read<OrderDto>(await anna.Client.PostAsJsonAsync($"/api/orders/{order.Id}/cancel", new { reason = "Ordered by mistake" }, Ct));
        var again = await anna.Client.PostAsJsonAsync($"/api/orders/{order.Id}/cancel", new { reason = "Again" }, Ct);

        cancelled.Status.ShouldBe(OrderStatus.Cancelled);
        cancelled.CancellationReason.ShouldBe("Ordered by mistake");
        cancelled.CancelledAt.ShouldBe(FuelFlowApiFactory.StartTime.AddMinutes(30));
        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        again.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Cancel_WithoutReason_Returns400AndOrderStaysPending()
    {
        var anna = await RegisterCustomerAsync();
        var productId = await SeedProductAsync();
        var address = await CreateAddressAsync(anna.Client);
        var order = await Read<OrderDto>(await anna.Client.PostAsJsonAsync("/api/orders", OrderBody(productId, address.Id, 1000), Ct));

        var response = await anna.Client.PostAsJsonAsync($"/api/orders/{order.Id}/cancel", new { reason = " " }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Read<OrderDto>(await anna.Client.GetAsync($"/api/orders/{order.Id}", Ct))).Status.ShouldBe(OrderStatus.Pending);
    }

    [Fact]
    public async Task OrderEndpoints_Anonymous_Return401()
    {
        using var anonymous = Factory.CreateClient();

        (await anonymous.GetAsync("/api/orders", Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsJsonAsync("/api/quotes", new { }, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}

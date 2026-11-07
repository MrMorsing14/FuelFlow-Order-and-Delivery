using System.Net;
using System.Net.Http.Json;
using FuelFlow.Application.Orders;
using FuelFlow.Application.Products;
using FuelFlow.Domain.Products;
using FuelFlow.Domain.Users;
using Shouldly;

namespace FuelFlow.Api.IntegrationTests;

public class ProductApiTests(FuelFlowApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task List_Anonymous_ReturnsCurrentPricesExAndIncVat()
    {
        await SeedProductAsync(ProductType.Diesel, 11.50m);
        using var anonymous = Factory.CreateClient();

        var products = await Read<List<ProductDto>>(await anonymous.GetAsync("/api/products", Ct));

        var diesel = products.ShouldHaveSingleItem();
        diesel.Type.ShouldBe(ProductType.Diesel);
        diesel.PricePerLitreExVat.ShouldBe(11.50m);
        diesel.PricePerLitreIncVat.ShouldBe(14.38m);
    }

    [Fact]
    public async Task ChangePrice_Admin_OldOrdersKeepTheirPriceAndNewOrdersUseTheNewOne()
    {
        var anna = await RegisterCustomerAsync();
        var admin = await StaffClientAsync(UserRole.Admin);
        var productId = await SeedProductAsync(pricePerLitre: 10m);
        var address = await CreateAddressAsync(anna.Client);
        var request = new { productId, deliveryAddressId = address.Id, litres = 1000 };
        var before = await Read<OrderDto>(await anna.Client.PostAsJsonAsync("/api/orders", request, Ct));

        var change = await Read<PriceChangeDto>(await admin.PutAsJsonAsync($"/api/products/{productId}/price", new { pricePerLitreExVat = 12m }, Ct));
        var after = await Read<OrderDto>(await anna.Client.PostAsJsonAsync("/api/orders", request, Ct));

        change.ValidFrom.ShouldBe(FuelFlowApiFactory.StartTime);
        before.Price.UnitPriceExVat.ShouldBe(10m);
        after.Price.UnitPriceExVat.ShouldBe(12m);
        (await Read<OrderDto>(await anna.Client.GetAsync($"/api/orders/{before.Id}", Ct))).Price.UnitPriceExVat.ShouldBe(10m);
    }

    [Fact]
    public async Task ChangePrice_FutureValidFrom_AppliesOnlyFromThatTime()
    {
        var admin = await StaffClientAsync(UserRole.Admin);
        var productId = await SeedProductAsync(pricePerLitre: 10m);
        var validFrom = FuelFlowApiFactory.StartTime.AddDays(1);

        (await admin.PutAsJsonAsync($"/api/products/{productId}/price", new { pricePerLitreExVat = 12m, validFrom }, Ct)).EnsureSuccessStatusCode();

        (await Read<List<ProductDto>>(await admin.GetAsync("/api/products", Ct))).Single().PricePerLitreExVat.ShouldBe(10m);
        Factory.Time.Advance(TimeSpan.FromDays(1));
        (await Read<List<ProductDto>>(await admin.GetAsync("/api/products", Ct))).Single().PricePerLitreExVat.ShouldBe(12m);
    }

    [Fact]
    public async Task ChangePrice_InThePastOrNonPositive_Returns400()
    {
        var admin = await StaffClientAsync(UserRole.Admin);
        var productId = await SeedProductAsync();

        var past = await admin.PutAsJsonAsync($"/api/products/{productId}/price",
            new { pricePerLitreExVat = 12m, validFrom = FuelFlowApiFactory.StartTime.AddMinutes(-1) }, Ct);
        var zero = await admin.PutAsJsonAsync($"/api/products/{productId}/price", new { pricePerLitreExVat = 0m }, Ct);

        past.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        zero.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ChangePrice_UnknownProduct_Returns404()
    {
        var admin = await StaffClientAsync(UserRole.Admin);

        var response = await admin.PutAsJsonAsync($"/api/products/{Guid.NewGuid()}/price", new { pricePerLitreExVat = 12m }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ChangePrice_CustomerDispatcherAndAnonymous_AreRejected()
    {
        var productId = await SeedProductAsync();
        var body = new { pricePerLitreExVat = 12m };
        var customer = await RegisterCustomerAsync();
        var dispatcher = await StaffClientAsync(UserRole.Dispatcher);
        using var anonymous = Factory.CreateClient();

        (await customer.Client.PutAsJsonAsync($"/api/products/{productId}/price", body, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await dispatcher.PutAsJsonAsync($"/api/products/{productId}/price", body, Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await anonymous.PutAsJsonAsync($"/api/products/{productId}/price", body, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}

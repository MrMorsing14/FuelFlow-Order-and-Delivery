using System.Net;
using System.Net.Http.Json;
using FuelFlow.Application.Customers;
using FuelFlow.Domain.Customers;
using FuelFlow.Domain.Users;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace FuelFlow.Api.IntegrationTests;

public class CustomerApiTests(FuelFlowApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task GetMe_RegisteredPrivateCustomer_ReturnsOwnProfile()
    {
        var anna = await RegisterCustomerAsync();

        var me = await Read<CustomerDto>(await anna.Client.GetAsync("/api/customers/me", Ct));

        me.Id.ShouldBe(anna.CustomerId);
        me.Type.ShouldBe(CustomerType.Private);
        me.Email.ShouldBe("anna@example.com");
        me.FullName.ShouldBe("Anna Hansen");
    }

    [Fact]
    public async Task UpdateMe_PrivateCustomer_ChangesPhoneAndName()
    {
        var anna = await RegisterCustomerAsync();

        var updated = await Read<CustomerDto>(await anna.Client.PutAsJsonAsync(
            "/api/customers/me", new { phone = "+4500000000", fullName = "Anna Jensen" }, Ct));

        updated.Phone.ShouldBe("+4500000000");
        (await Read<CustomerDto>(await anna.Client.GetAsync("/api/customers/me", Ct))).FullName.ShouldBe("Anna Jensen");
    }

    [Fact]
    public async Task UpdateMe_BusinessCustomerWithInvalidCvr_Returns400AndKeepsOldData()
    {
        var business = await RegisterCustomerAsync("kontor@jensen.example", business: true);

        var response = await business.Client.PutAsJsonAsync(
            "/api/customers/me", new { phone = "+4500000000", companyName = "New Name", cvr = "12345678" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var me = await Read<CustomerDto>(await business.Client.GetAsync("/api/customers/me", Ct));
        me.CompanyName.ShouldBe("Jensen ApS");
        me.Phone.ShouldBe("+4587654321");
    }

    [Fact]
    public async Task UpdateMe_EmptyPhone_Returns400ListingTheField()
    {
        var anna = await RegisterCustomerAsync();

        var response = await anna.Client.PutAsJsonAsync("/api/customers/me", new { phone = "", fullName = "Anna" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(Ct))!.Errors.Keys.ShouldContain("phone");
    }

    [Fact]
    public async Task Addresses_CreateReadUpdateDelete_FullLifecycle()
    {
        var anna = await RegisterCustomerAsync();

        var created = await CreateAddressAsync(anna.Client, tankCapacityLitres: 3000);
        (await Read<List<AddressDto>>(await anna.Client.GetAsync("/api/customers/me/addresses", Ct))).Select(a => a.Id).ShouldBe([created.Id]);

        var updated = await Read<AddressDto>(await anna.Client.PutAsJsonAsync($"/api/customers/me/addresses/{created.Id}", new
        {
            street = "Ny Vej 2",
            postalCode = "8000",
            city = "Aarhus C",
            tankCapacityLitres = 4000,
        }, Ct));
        updated.Street.ShouldBe("Ny Vej 2");
        updated.AccessNotes.ShouldBeNull();
        (await Read<AddressDto>(await anna.Client.GetAsync($"/api/customers/me/addresses/{created.Id}", Ct))).TankCapacityLitres.ShouldBe(4000);

        (await anna.Client.DeleteAsync($"/api/customers/me/addresses/{created.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await Read<List<AddressDto>>(await anna.Client.GetAsync("/api/customers/me/addresses", Ct))).ShouldBeEmpty();
    }

    [Fact]
    public async Task CreateAddress_Valid_Returns201WithLocationHeader()
    {
        var anna = await RegisterCustomerAsync();

        var response = await anna.Client.PostAsJsonAsync("/api/customers/me/addresses",
            new { street = "Vejen 1", postalCode = "2100", city = "København Ø", tankCapacityLitres = 3000 }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var address = (await response.Content.ReadFromJsonAsync<AddressDto>(Json, Ct))!;
        response.Headers.Location!.AbsolutePath.ShouldBe($"/api/customers/me/addresses/{address.Id}");
    }

    [Fact]
    public async Task CreateAddress_InvalidInput_Returns400ListingFieldErrors()
    {
        var anna = await RegisterCustomerAsync();

        var response = await anna.Client.PostAsJsonAsync("/api/customers/me/addresses",
            new { street = "", postalCode = "123", city = "København", tankCapacityLitres = 0 }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = (await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(Ct))!;
        problem.Errors.Keys.ShouldBe(["street", "postalCode", "tankCapacityLitres"], ignoreOrder: true);
    }

    [Fact]
    public async Task Address_OfAnotherCustomer_IsNotFoundForReadUpdateAndDelete()
    {
        var anna = await RegisterCustomerAsync("anna@example.com");
        var ben = await RegisterCustomerAsync("ben@example.com");
        var annasAddress = await CreateAddressAsync(anna.Client);
        var url = $"/api/customers/me/addresses/{annasAddress.Id}";
        var body = new { street = "Hacked 1", postalCode = "2100", city = "X", tankCapacityLitres = 1000 };

        (await ben.Client.GetAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ben.Client.PutAsJsonAsync(url, body, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ben.Client.DeleteAsync(url, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await Read<AddressDto>(await anna.Client.GetAsync(url, Ct))).Street.ShouldBe("Vejen 1");
    }

    [Fact]
    public async Task DeleteAddress_WithOrders_Returns409AndKeepsAddress()
    {
        var anna = await RegisterCustomerAsync();
        var productId = await SeedProductAsync();
        var address = await CreateAddressAsync(anna.Client);
        (await anna.Client.PostAsJsonAsync("/api/orders", new { productId, deliveryAddressId = address.Id, litres = 1000 }, Ct))
            .EnsureSuccessStatusCode();

        var response = await anna.Client.DeleteAsync($"/api/customers/me/addresses/{address.Id}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        (await anna.Client.GetAsync($"/api/customers/me/addresses/{address.Id}", Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CustomerEndpoints_StaffUser_Returns403()
    {
        var admin = await StaffClientAsync(UserRole.Admin);

        (await admin.GetAsync("/api/customers/me/addresses", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}

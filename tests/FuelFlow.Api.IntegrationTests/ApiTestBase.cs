using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FuelFlow.Application.Auth;
using FuelFlow.Application.Customers;
using FuelFlow.Domain.Products;
using FuelFlow.Domain.Users;
using Microsoft.Extensions.DependencyInjection;

namespace FuelFlow.Api.IntegrationTests;

/// <summary>Shared helpers for tests that call the API as a logged-in user.</summary>
public abstract class ApiTestBase(FuelFlowApiFactory factory) : IntegrationTestBase(factory)
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    protected static string Password => "correct-horse-battery";

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected sealed record Session(HttpClient Client, Guid UserId, Guid CustomerId);

    protected async Task<Session> RegisterCustomerAsync(string email = "anna@example.com", bool business = false)
    {
        var client = Factory.CreateClient();
        object body = business
            ? new { email, password = Password, type = "Business", phone = "+4587654321", companyName = "Jensen ApS", cvr = "10150817" }
            : new { email, password = Password, type = "Private", phone = "+4512345678", fullName = "Anna Hansen" };
        var registered = await Read<RegisterResponse>(await client.PostAsJsonAsync("/api/auth/register", body, Ct));
        await LoginAsync(client, email);
        return new Session(client, registered.UserId, registered.CustomerId);
    }

    protected async Task<HttpClient> StaffClientAsync(UserRole role, string email = "staff@example.com")
    {
        await using (var scope = NewScope())
        {
            var hash = scope.ServiceProvider.GetRequiredService<IPasswordService>().Hash(Password);
            Db(scope).Users.Add(User.Create(email, hash, role));
            await Db(scope).SaveChangesAsync(Ct);
        }

        var client = Factory.CreateClient();
        await LoginAsync(client, email);
        return client;
    }

    protected async Task<Guid> SeedProductAsync(ProductType type = ProductType.Diesel, decimal pricePerLitre = 10m)
    {
        await using var scope = NewScope();
        var product = Product.Create(type, pricePerLitre, FuelFlowApiFactory.StartTime.AddDays(-1));
        Db(scope).Products.Add(product);
        await Db(scope).SaveChangesAsync(Ct);
        return product.Id;
    }

    protected static async Task<AddressDto> CreateAddressAsync(HttpClient client, int tankCapacityLitres = 10_000, string postalCode = "2100") =>
        await Read<AddressDto>(await client.PostAsJsonAsync("/api/customers/me/addresses", new
        {
            street = "Vejen 1",
            postalCode,
            city = "København Ø",
            tankCapacityLitres,
            accessNotes = "Tank behind garage",
        }, Ct));

    protected static async Task<T> Read<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(Json, Ct))!;
    }

    private static async Task LoginAsync(HttpClient client, string email)
    {
        var login = await Read<LoginResponse>(await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password }, Ct));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
    }
}

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FuelFlow.Web.Api;

/// <summary>Every call the UI makes to the API. Turns error answers into <see cref="ApiException"/>.</summary>
public sealed class ApiClient(HttpClient http, AuthSession session)
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    // ---- auth
    public Task<LoginResponse> LoginAsync(string email, string password) =>
        SendAsync<LoginResponse>(HttpMethod.Post, "api/auth/login", new { email, password });

    public Task RegisterAsync(RegisterRequest request) => SendAsync(HttpMethod.Post, "api/auth/register", request);

    // ---- customer
    public Task<List<Product>> GetProductsAsync() => SendAsync<List<Product>>(HttpMethod.Get, "api/products");

    public Task<Customer> GetProfileAsync() => SendAsync<Customer>(HttpMethod.Get, "api/customers/me");

    public Task<List<Address>> GetAddressesAsync() => SendAsync<List<Address>>(HttpMethod.Get, "api/customers/me/addresses");

    public Task CreateAddressAsync(AddressRequest request) => SendAsync(HttpMethod.Post, "api/customers/me/addresses", request);

    public Task UpdateAddressAsync(Guid id, AddressRequest request) => SendAsync(HttpMethod.Put, $"api/customers/me/addresses/{id}", request);

    public Task DeleteAddressAsync(Guid id) => SendAsync(HttpMethod.Delete, $"api/customers/me/addresses/{id}");

    public Task<Price> QuoteAsync(OrderRequest request) => SendAsync<Price>(HttpMethod.Post, "api/quotes", request);

    public Task<Order> PlaceOrderAsync(OrderRequest request) => SendAsync<Order>(HttpMethod.Post, "api/orders", request);

    public Task<Paged<Order>> GetOrdersAsync(int page, int pageSize) =>
        SendAsync<Paged<Order>>(HttpMethod.Get, $"api/orders?page={page}&pageSize={pageSize}");

    public Task CancelOrderAsync(Guid id, string reason) => SendAsync(HttpMethod.Post, $"api/orders/{id}/cancel", new { reason });

    // ---- dispatch
    public Task<List<Region>> GetRegionsAsync() => SendAsync<List<Region>>(HttpMethod.Get, "api/dispatch/regions");

    public Task<Paged<DispatchOrder>> GetDispatchOrdersAsync(string? status, string? region, string? date, int page, int pageSize)
    {
        var query = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        AddIfSet(query, "status", status);
        AddIfSet(query, "region", region);
        AddIfSet(query, "date", date);
        return SendAsync<Paged<DispatchOrder>>(HttpMethod.Get, $"api/dispatch/orders?{string.Join('&', query)}");
    }

    public Task ConfirmAsync(Guid id) => SendAsync(HttpMethod.Post, $"api/dispatch/orders/{id}/confirm");

    public Task StartDeliveryAsync(Guid id) => SendAsync(HttpMethod.Post, $"api/dispatch/orders/{id}/out-for-delivery");

    public Task DeliverAsync(Guid id, int deliveredLitres, string? reason) =>
        SendAsync(HttpMethod.Post, $"api/dispatch/orders/{id}/deliver", new { deliveredLitres, reason });

    private static void AddIfSet(List<string> query, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            query.Add($"{name}={Uri.EscapeDataString(value)}");
        }
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string url, object? body = null)
    {
        using var response = await SendCoreAsync(method, url, body);
        return (await response.Content.ReadFromJsonAsync<T>(_json))!;
    }

    private async Task SendAsync(HttpMethod method, string url, object? body = null)
    {
        using var response = await SendCoreAsync(method, url, body);
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string url, object? body)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: _json);
        }

        if (session.Token is { } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request);
        }
        catch (HttpRequestException)
        {
            throw new ApiException(0, "Cannot reach the server. Check your connection and try again.");
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && session.Token is not null)
            {
                // The token was rejected (usually expired): forget it, so the UI sends the user to the login page.
                await session.SignOutAsync();
                throw new ApiException(401, "Your session has expired. Please log in again.");
            }

            throw await ToExceptionAsync(response);
        }
    }

    private static async Task<ApiException> ToExceptionAsync(HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(_json);
            var message = problem?.Detail ?? problem?.Title ?? $"The request failed ({status}).";
            return new ApiException(status, message, problem?.Errors);
        }
        catch (JsonException)
        {
            return new ApiException(status, $"The request failed ({status}).");
        }
    }

    private sealed record ProblemBody(string? Title, string? Detail, Dictionary<string, string[]>? Errors);
}

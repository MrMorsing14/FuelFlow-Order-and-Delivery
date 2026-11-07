using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Bunit;
using FuelFlow.Web.Api;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace FuelFlow.Web.UnitTests;

/// <summary>Answers the UI's HTTP calls from a list of canned responses and remembers what was asked.</summary>
public sealed class StubHandler : HttpMessageHandler
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private readonly List<(HttpMethod Method, string PathPrefix, Func<HttpResponseMessage> Respond)> _routes = [];

    public List<Request> Requests { get; } = [];

    public sealed record Request(HttpMethod Method, string PathAndQuery, string? Body, string? Authorization);

    public void On(HttpMethod method, string pathPrefix, object? body = null, HttpStatusCode status = HttpStatusCode.OK) =>
        _routes.Add((method, pathPrefix, () => new HttpResponseMessage(status) { Content = JsonContent.Create(body, options: _json) }));

    public void OnProblem(HttpMethod method, string pathPrefix, HttpStatusCode status, string json) =>
        _routes.Add((method, pathPrefix, () => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/problem+json") }));

    public void OnNetworkFailure(HttpMethod method, string pathPrefix) =>
        _routes.Add((method, pathPrefix, () => throw new HttpRequestException("connection refused")));

    public IEnumerable<Request> Calls(HttpMethod method, string pathPrefix) =>
        Requests.Where(r => r.Method == method && r.PathAndQuery.StartsWith(pathPrefix, StringComparison.Ordinal));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var pathAndQuery = request.RequestUri!.PathAndQuery.TrimStart('/');
        Requests.Add(new Request(request.Method, pathAndQuery, body, request.Headers.Authorization?.ToString()));

        var route = _routes.FirstOrDefault(r => r.Method == request.Method && pathAndQuery.StartsWith(r.PathPrefix, StringComparison.Ordinal));
        return route.Respond is null
            ? throw new InvalidOperationException($"The test did not expect {request.Method} {pathAndQuery}")
            : route.Respond();
    }
}

/// <summary>A bUnit context wired like the real app: API client, login session, fake browser storage, fake clock.</summary>
public sealed class TestApp : BunitContext
{
    public static readonly DateTimeOffset Now = new(2026, 3, 2, 8, 0, 0, TimeSpan.Zero);

    public TestApp()
    {
        JSInterop.Mode = JSRuntimeMode.Loose; // sessionStorage calls and PageTitle work without setup
        Services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        Services.AddSingleton(new HttpClient(Http) { BaseAddress = new Uri("http://localhost/") });
        Services.AddScoped<AuthSession>();
        Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<AuthSession>());
        Services.AddScoped<ApiClient>();
    }

    public StubHandler Http { get; } = new();

    public AuthSession Session => Services.GetRequiredService<AuthSession>();

    public ApiClient Api => Services.GetRequiredService<ApiClient>();

    public Task SignInAsync(string role = "Customer") => Session.SignInAsync(Jwt.Create(role, Now.AddMinutes(60)));
}

public static class Jwt
{
    /// <summary>An unsigned JWT with the claims the API puts in real ones. The UI never checks the signature.</summary>
    public static string Create(string role, DateTimeOffset expires, string email = "anna@example.com")
    {
        var payload = new Dictionary<string, object>
        {
            ["sub"] = "11111111-1111-1111-1111-111111111111",
            ["email"] = email,
            ["role"] = role,
            ["exp"] = expires.ToUnixTimeSeconds(),
            ["x"] = "????>>>>", // encodes to base64 with + and /, which JWTs write as - and _
        };
        return $"{Encode(new { alg = "HS256", typ = "JWT" })}.{Encode(payload)}.signature";
    }

    private static string Encode(object value) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace FuelFlow.Api.IntegrationTests;

public class RateLimitTests(FuelFlowApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Login_MoreAttemptsThanAllowed_Returns429ProblemDetails()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var limited = Factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimiting:AuthPermitLimit"] = "3" })));
        using var client = limited.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "a@b.dk", password = "wrong" }, ct);
            statuses.Add(response.StatusCode);
            if (i == 3)
            {
                response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
            }
        }

        statuses.ShouldBe([HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests]);
    }

    [Fact]
    public async Task Login_BehindAProxy_EachClientAddressHasItsOwnLimit()
    {
        var ct = TestContext.Current.CancellationToken;
        // ASPNETCORE_FORWARDEDHEADERS_ENABLED=true is what the compose file and the Kubernetes manifest will set.
        await using var behindProxy = Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("FORWARDEDHEADERS_ENABLED", "true");
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimiting:AuthPermitLimit"] = "3" }));
        });
        using var client = behindProxy.CreateClient();

        async Task<HttpStatusCode> LoginAs(string clientAddress)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(new { email = "a@b.dk", password = "wrong" }),
            };
            request.Headers.Add("X-Forwarded-For", clientAddress);
            return (await client.SendAsync(request, ct)).StatusCode;
        }

        for (var i = 0; i < 3; i++)
        {
            (await LoginAs("203.0.113.1")).ShouldBe(HttpStatusCode.Unauthorized);
        }

        (await LoginAs("203.0.113.1")).ShouldBe(HttpStatusCode.TooManyRequests);
        (await LoginAs("203.0.113.2")).ShouldBe(HttpStatusCode.Unauthorized);
    }
}

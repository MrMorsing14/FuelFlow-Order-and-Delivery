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
}

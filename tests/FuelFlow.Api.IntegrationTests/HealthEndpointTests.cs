using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;

namespace FuelFlow.Api.IntegrationTests;

// WebApplicationFactory boots the real app in-memory (like @SpringBootTest + MockMvc/TestRestTemplate).
// .NET 10 generates a public Program class for top-level-statement apps, so it can be used directly.
public class HealthEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task GetLive_WhenAppRuns_Returns200()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}

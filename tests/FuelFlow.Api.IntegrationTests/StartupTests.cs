using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Shouldly;

namespace FuelFlow.Api.IntegrationTests;

public class StartupTests(FuelFlowApiFactory factory) : IntegrationTestBase(factory)
{
    [Fact]
    public async Task Startup_MissingConnectionString_FailsFast()
    {
        await using var broken = Factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = "" })));

        Should.Throw<InvalidOperationException>(() => broken.CreateClient())
            .Message.ShouldContain("Connection string");
    }
}

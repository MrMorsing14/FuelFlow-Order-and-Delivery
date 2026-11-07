using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
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

    [Theory]
    [InlineData("")]
    [InlineData("too-short-key")] // 13 bytes, needs 32
    public void Startup_MissingOrShortJwtKey_FailsFast(string key)
    {
        using var broken = Factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:SigningKey"] = key })));

        Should.Throw<OptionsValidationException>(() => broken.CreateClient())
            .Message.ShouldContain("Jwt:SigningKey");
    }
}

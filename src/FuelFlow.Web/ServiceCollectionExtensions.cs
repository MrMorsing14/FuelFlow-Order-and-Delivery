using FuelFlow.Web.Api;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace FuelFlow.Web;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Everything the pages need. Program.cs and the tests both call this, so a forgotten registration
    /// shows up in the tests instead of as a blank page in the browser.
    /// </summary>
    public static IServiceCollection AddFuelFlowWeb(this IServiceCollection services, Uri apiBaseAddress)
    {
        services.AddScoped(_ => new HttpClient { BaseAddress = apiBaseAddress });
        services.AddSingleton(TimeProvider.System);

        services.AddAuthorizationCore();
        services.AddScoped<AuthSession>();
        services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<AuthSession>());
        services.AddScoped<ApiClient>();
        return services;
    }
}

using FluentValidation;
using FuelFlow.Application.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace FuelFlow.Application;

public static class ApplicationServiceExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(AssemblyMarker).Assembly);
        services.AddScoped<AuthService>();
        return services;
    }
}

using FuelFlow.Application.Auth;
using FuelFlow.Domain.Users;
using FuelFlow.Infrastructure.Auth;
using FuelFlow.Infrastructure.Persistence;
using FuelFlow.Infrastructure.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FuelFlow.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Default";

    /// <summary>
    /// Extension method that registers this layer's services (Spring equivalent: a @Configuration class).
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        // The options lambda receives the built IServiceProvider, so the connection string is read from the
        // final configuration (appsettings, env vars, and test overrides) at first use.
        services.AddDbContext<FuelFlowDbContext>((sp, options) =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName)
                ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");
            options.UseNpgsql(connectionString);
        });

        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<FuelFlowDbContext>());
        services.AddJwtAuth();

        services.AddOptions<SeedOptions>().BindConfiguration(SeedOptions.SectionName);
        services.AddScoped<DevelopmentDataSeeder>();
        return services;
    }
}

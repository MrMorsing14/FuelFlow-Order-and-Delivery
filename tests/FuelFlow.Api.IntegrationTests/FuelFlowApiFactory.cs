using FuelFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace FuelFlow.Api.IntegrationTests;

/// <summary>
/// Boots the real API in memory against a real PostgreSQL container (Testcontainers).
/// One instance is shared by all tests in the "Database" collection (see <see cref="DatabaseCollection"/>),
/// so the container starts once. Tests call <see cref="ResetDatabaseAsync"/> to start from empty tables.
/// </summary>
public sealed class FuelFlowApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    public string ConnectionString => _postgres.GetConnectionString();

    // xUnit v3 calls this once before the first test in the collection.
    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        // "Testing" is not "Development", so the app does not migrate or seed on its own.
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<FuelFlowDbContext>().Database.MigrateAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = ConnectionString,
            }));
    }

    /// <summary>Deletes all rows in every table except the EF migrations history.</summary>
    public async Task ResetDatabaseAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FuelFlowDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            DO $$
            DECLARE t text;
            BEGIN
              FOR t IN SELECT tablename FROM pg_tables
                       WHERE schemaname = 'public' AND tablename <> '__EFMigrationsHistory'
              LOOP
                EXECUTE 'TRUNCATE TABLE ' || quote_ident(t) || ' CASCADE';
              END LOOP;
            END $$;
            """);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

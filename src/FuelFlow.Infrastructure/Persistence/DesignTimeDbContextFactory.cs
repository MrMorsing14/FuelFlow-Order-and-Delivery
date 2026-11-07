using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FuelFlow.Infrastructure.Persistence;

/// <summary>
/// Used only by `dotnet ef` (migrations) so the tool does not need to boot the whole API.
/// Override the connection with the ConnectionStrings__Default environment variable.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FuelFlowDbContext>
{
    public FuelFlowDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Database=fuelflow;Username=fuelflow;Password=fuelflow";

        var options = new DbContextOptionsBuilder<FuelFlowDbContext>().UseNpgsql(connectionString).Options;
        return new FuelFlowDbContext(options);
    }
}

using FuelFlow.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace FuelFlow.Api.IntegrationTests;

/// <summary>Resets the database before every test (xUnit creates a new class instance per test).</summary>
[Collection(DatabaseCollection.Name)]
public abstract class IntegrationTestBase(FuelFlowApiFactory factory) : IAsyncLifetime
{
    protected FuelFlowApiFactory Factory { get; } = factory;

    public ValueTask InitializeAsync() => new(Factory.ResetDatabaseAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>A fresh DbContext, like a new EntityManager, so reads never come from a stale first-level cache.</summary>
    protected AsyncServiceScope NewScope() => Factory.Services.CreateAsyncScope();

    protected static FuelFlowDbContext Db(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<FuelFlowDbContext>();
}

namespace FuelFlow.Api.IntegrationTests;

/// <summary>
/// An xUnit "collection" shares one fixture instance (the factory + container) between test classes
/// and runs those classes sequentially, so tests never see each other's data.
/// </summary>
[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<FuelFlowApiFactory>
{
    public const string Name = "Database";
}

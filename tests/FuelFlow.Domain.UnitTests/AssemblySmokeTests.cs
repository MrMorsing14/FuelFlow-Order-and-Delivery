using Shouldly;

namespace FuelFlow.Domain.UnitTests;

public class AssemblySmokeTests
{
    [Fact]
    public void AssemblyMarker_WhenLoaded_HasExpectedAssemblyName()
    {
        typeof(AssemblyMarker).Assembly.GetName().Name.ShouldBe("FuelFlow.Domain");
    }
}

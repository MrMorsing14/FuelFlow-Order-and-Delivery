using NSubstitute;
using Shouldly;

namespace FuelFlow.Application.UnitTests;

public class AssemblySmokeTests
{
    [Fact]
    public void AssemblyMarker_WhenLoaded_HasExpectedAssemblyName()
    {
        typeof(AssemblyMarker).Assembly.GetName().Name.ShouldBe("FuelFlow.Application");
    }

    [Fact]
    public void Substitute_WhenConfigured_ReturnsStubbedValue()
    {
        var clock = Substitute.For<TimeProvider>();
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        clock.GetUtcNow().Returns(now);

        clock.GetUtcNow().ShouldBe(now);
    }
}

using FuelFlow.Domain.Common;
using FuelFlow.Domain.Scheduling;
using Shouldly;

namespace FuelFlow.Domain.UnitTests;

public class RegionMapTests
{
    private static RegionMap Map() => new(
    [
        new RegionRange { Name = "Copenhagen", From = 1000, To = 2999 },
        new RegionRange { Name = "Zealand", From = 3000, To = 4999 },
    ]);

    [Theory]
    [InlineData("1000", "Copenhagen")]
    [InlineData("2999", "Copenhagen")]
    [InlineData("3000", "Zealand")]
    [InlineData("4999", "Zealand")]
    [InlineData("5000", null)]
    public void RegionOf_PostalCode_UsesInclusiveRanges(string postalCode, string? expected)
    {
        Map().RegionOf(PostalCode.Create(postalCode)).ShouldBe(expected);
    }

    [Fact]
    public void Find_Name_IgnoresCaseAndReturnsConfiguredSpelling()
    {
        Map().Find(" copenhagen ")!.Name.ShouldBe("Copenhagen");
        Map().Find("Mars").ShouldBeNull();
        Map().Find(null).ShouldBeNull();
    }

    [Fact]
    public void Constructor_NoRegions_Throws()
    {
        Should.Throw<DomainValidationException>(() => new RegionMap([]));
    }

    [Theory]
    [InlineData("", 1000, 2000)]       // no name
    [InlineData("A", 2000, 1000)]      // From > To
    [InlineData("A", 999, 2000)]       // not a Danish postal code
    [InlineData("A", 1000, 10000)]
    public void Constructor_InvalidRange_Throws(string name, int from, int to)
    {
        Should.Throw<DomainValidationException>(() => new RegionMap([new RegionRange { Name = name, From = from, To = to }]));
    }

    [Fact]
    public void Constructor_OverlappingOrDuplicateRegions_Throws()
    {
        Should.Throw<DomainValidationException>(() => new RegionMap(
        [
            new RegionRange { Name = "A", From = 1000, To = 2000 },
            new RegionRange { Name = "B", From = 2000, To = 3000 },
        ]));
        Should.Throw<DomainValidationException>(() => new RegionMap(
        [
            new RegionRange { Name = "A", From = 1000, To = 1999 },
            new RegionRange { Name = "a", From = 3000, To = 3999 },
        ]));
    }
}

using FuelFlow.Domain.Common;
using Shouldly;

namespace FuelFlow.Domain.UnitTests;

public class ValueObjectTests
{
    [Theory]
    [InlineData("10150817")]
    [InlineData("13585628")]
    [InlineData(" 10150817 ")]
    public void CvrNumber_ValidNumber_IsAccepted(string raw)
    {
        CvrNumber.TryCreate(raw, out var cvr).ShouldBeTrue();
        cvr.Value.ShouldBe(raw.Trim());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1015081")]       // 7 digits
    [InlineData("101508170")]     // 9 digits
    [InlineData("1015081A")]      // not all digits
    [InlineData("10150818")]      // fails modulus-11 check
    [InlineData("00000001")]      // fails modulus-11 check
    public void CvrNumber_InvalidNumber_IsRejected(string? raw)
    {
        CvrNumber.TryCreate(raw, out _).ShouldBeFalse();
        Should.Throw<DomainValidationException>(() => CvrNumber.Create(raw));
    }

    [Theory]
    [InlineData("1000")]
    [InlineData("2100")]
    [InlineData("9990")]
    public void PostalCode_ValidCode_IsAccepted(string raw)
    {
        PostalCode.Create(raw).Value.ShouldBe(raw);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("999")]       // too short
    [InlineData("10000")]     // too long
    [InlineData("0999")]      // Danish codes start at 1000
    [InlineData("21A0")]
    public void PostalCode_InvalidCode_IsRejected(string? raw)
    {
        PostalCode.TryCreate(raw, out _).ShouldBeFalse();
        Should.Throw<DomainValidationException>(() => PostalCode.Create(raw));
    }

    [Fact]
    public void PostalCode_SameValue_AreEqual()
    {
        PostalCode.Create("2100").ShouldBe(PostalCode.Create("2100"));
        PostalCode.Create("2100").AsNumber().ShouldBe(2100);
    }
}

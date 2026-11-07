using FuelFlow.Application.Auth;
using FuelFlow.Domain.Customers;
using Shouldly;

namespace FuelFlow.Application.UnitTests;

public class RegisterRequestValidatorTests
{
    private readonly RegisterRequestValidator _validator = new();

    private static RegisterRequest Private() =>
        new("anna@example.com", "correct-horse", CustomerType.Private, "+4512345678", "Anna Hansen", null, null);

    private static RegisterRequest Business() =>
        new("a@b.dk", "correct-horse", CustomerType.Business, "+4511111111", null, "Jensen ApS", "10150817");

    [Fact]
    public void Validate_ValidPrivateAndBusiness_Passes()
    {
        _validator.Validate(Private()).IsValid.ShouldBeTrue();
        _validator.Validate(Business()).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public void Validate_BadEmail_FailsOnEmail(string email)
    {
        _validator.Validate(Private() with { Email = email }).Errors.ShouldContain(e => e.PropertyName == "Email");
    }

    [Theory]
    [InlineData("")]
    [InlineData("1234567")] // 7 chars
    public void Validate_ShortPassword_FailsOnPassword(string password)
    {
        _validator.Validate(Private() with { Password = password }).Errors.ShouldContain(e => e.PropertyName == "Password");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Validate_PrivateWithoutFullName_FailsOnFullName(string? name)
    {
        _validator.Validate(Private() with { FullName = name }).Errors.ShouldContain(e => e.PropertyName == "FullName");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("1234567")]    // too short
    [InlineData("12345678")]   // fails modulus-11 check
    public void Validate_BusinessWithInvalidCvr_FailsOnCvr(string? cvr)
    {
        _validator.Validate(Business() with { Cvr = cvr }).Errors.ShouldContain(e => e.PropertyName == "Cvr");
    }

    [Fact]
    public void Validate_BusinessWithoutCompanyName_FailsOnCompanyName()
    {
        _validator.Validate(Business() with { CompanyName = "" }).Errors.ShouldContain(e => e.PropertyName == "CompanyName");
    }

    [Fact]
    public void Validate_UnknownCustomerType_Fails()
    {
        _validator.Validate(Private() with { Type = (CustomerType)99 }).IsValid.ShouldBeFalse();
    }
}

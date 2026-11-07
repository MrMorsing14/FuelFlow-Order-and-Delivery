using FluentValidation;
using FuelFlow.Domain.Common;

namespace FuelFlow.Application.Customers;

public sealed class UpdateCustomerRequestValidator : AbstractValidator<UpdateCustomerRequest>
{
    // Name / company / CVR rules depend on the customer's type and are enforced by Customer.Update.
    public UpdateCustomerRequestValidator()
    {
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(50);
        RuleFor(x => x.FullName).MaximumLength(200);
        RuleFor(x => x.CompanyName).MaximumLength(200);
    }
}

public sealed class AddressRequestValidator : AbstractValidator<AddressRequest>
{
    public AddressRequestValidator()
    {
        RuleFor(x => x.Street).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PostalCode)
            .Must(code => PostalCode.TryCreate(code, out _))
            .WithMessage("Postal code must be 4 digits (1000-9999).");
        RuleFor(x => x.City).NotEmpty().MaximumLength(100);
        RuleFor(x => x.TankCapacityLitres).GreaterThan(0);
        RuleFor(x => x.AccessNotes).MaximumLength(500);
    }
}

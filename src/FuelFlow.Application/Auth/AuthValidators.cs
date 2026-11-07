using FluentValidation;
using FuelFlow.Domain.Common;
using FuelFlow.Domain.Customers;

namespace FuelFlow.Application.Auth;

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(320);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(128);
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(50);

        When(x => x.Type == CustomerType.Private, () =>
            RuleFor(x => x.FullName).NotEmpty().MaximumLength(200));

        When(x => x.Type == CustomerType.Business, () =>
        {
            RuleFor(x => x.CompanyName).NotEmpty().MaximumLength(200);
            RuleFor(x => x.Cvr)
                .Must(cvr => CvrNumber.TryCreate(cvr, out _))
                .WithMessage("CVR must be a valid 8-digit Danish CVR number.");
        });
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}

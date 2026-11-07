using FluentValidation;
using FuelFlow.Domain.Customers;
using FuelFlow.Domain.Users;

namespace FuelFlow.Application.Auth;

public sealed class AuthService(
    IUserRepository users,
    ICustomerRepository customers,
    IUnitOfWork unitOfWork,
    IPasswordService passwords,
    ITokenService tokens,
    IValidator<RegisterRequest> registerValidator,
    IValidator<LoginRequest> loginValidator)
{
    public async Task<RegisterResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        await registerValidator.ValidateAndThrowAsync(request, cancellationToken);

        var email = request.Email.Trim().ToLowerInvariant();
        if (await users.FindByEmailAsync(email, cancellationToken) is not null)
        {
            throw new EmailAlreadyRegisteredException();
        }

        var customer = request.Type == CustomerType.Private
            ? Customer.CreatePrivate(request.FullName, request.Phone, email)
            : Customer.CreateBusiness(request.CompanyName, request.Cvr, request.Phone, email);
        var user = User.Create(email, passwords.Hash(request.Password), UserRole.Customer, customer.Id);

        customers.Add(customer);
        users.Add(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new RegisterResponse(user.Id, customer.Id);
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        await loginValidator.ValidateAndThrowAsync(request, cancellationToken);

        var user = await users.FindByEmailAsync(request.Email.Trim().ToLowerInvariant(), cancellationToken);
        if (user is null || !passwords.Verify(user.PasswordHash, request.Password))
        {
            throw new AuthenticationFailedException();
        }

        var token = tokens.Create(user);
        return new LoginResponse(token.Value, "Bearer", token.ExpiresAt);
    }
}

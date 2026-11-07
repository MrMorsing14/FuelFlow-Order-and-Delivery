using FluentValidation;
using FuelFlow.Application.Auth;
using FuelFlow.Domain.Customers;
using FuelFlow.Domain.Users;
using NSubstitute;
using Shouldly;

namespace FuelFlow.Application.UnitTests;

public class AuthServiceTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IPasswordService _passwords = Substitute.For<IPasswordService>();
    private readonly ITokenService _tokens = Substitute.For<ITokenService>();
    private readonly AuthService _service;

    public AuthServiceTests()
    {
        _passwords.Hash(Arg.Any<string>()).Returns("hashed");
        _service = new AuthService(
            _users, _customers, _unitOfWork, _passwords, _tokens, new RegisterRequestValidator(), new LoginRequestValidator());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static RegisterRequest PrivateRequest(string email = " Anna@Example.com ") =>
        new(email, "correct-horse", CustomerType.Private, "+4512345678", "Anna Hansen", null, null);

    [Fact]
    public async Task RegisterAsync_ValidPrivateRequest_SavesLinkedCustomerAndUserWithHashedPassword()
    {
        User? savedUser = null;
        _users.When(u => u.Add(Arg.Any<User>())).Do(call => savedUser = call.Arg<User>());

        var response = await _service.RegisterAsync(PrivateRequest(), Ct);

        savedUser.ShouldNotBeNull();
        savedUser.Email.ShouldBe("anna@example.com");
        savedUser.PasswordHash.ShouldBe("hashed");
        savedUser.Role.ShouldBe(UserRole.Customer);
        savedUser.CustomerId.ShouldBe(response.CustomerId);
        _customers.Received(1).Add(Arg.Is<Customer>(c => c.Id == response.CustomerId && c.Type == CustomerType.Private));
        await _unitOfWork.Received(1).SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task RegisterAsync_BusinessRequest_CreatesBusinessCustomer()
    {
        var request = new RegisterRequest("a@b.dk", "correct-horse", CustomerType.Business, "+4511111111", null, "Jensen ApS", "10150817");

        await _service.RegisterAsync(request, Ct);

        _customers.Received(1).Add(Arg.Is<Customer>(c => c.Type == CustomerType.Business && c.CompanyName == "Jensen ApS"));
    }

    [Fact]
    public async Task RegisterAsync_EmailAlreadyTaken_ThrowsAndSavesNothing()
    {
        _users.FindByEmailAsync("anna@example.com", Arg.Any<CancellationToken>())
            .Returns(User.Create("anna@example.com", "h", UserRole.Admin));

        await Should.ThrowAsync<EmailAlreadyRegisteredException>(() => _service.RegisterAsync(PrivateRequest(), Ct));

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterAsync_InvalidRequest_ThrowsValidationAndSavesNothing()
    {
        var request = PrivateRequest() with { Password = "short" };

        await Should.ThrowAsync<ValidationException>(() => _service.RegisterAsync(request, Ct));

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoginAsync_CorrectPassword_ReturnsBearerToken()
    {
        var user = User.Create("anna@example.com", "hashed", UserRole.Customer, Guid.NewGuid());
        var expires = new DateTimeOffset(2026, 1, 1, 1, 0, 0, TimeSpan.Zero);
        _users.FindByEmailAsync("anna@example.com", Arg.Any<CancellationToken>()).Returns(user);
        _passwords.Verify("hashed", "correct-horse").Returns(true);
        _tokens.Create(user).Returns(new AccessToken("jwt", expires));

        var response = await _service.LoginAsync(new LoginRequest(" Anna@Example.com", "correct-horse"), Ct);

        response.ShouldBe(new LoginResponse("jwt", "Bearer", expires));
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_Throws()
    {
        var user = User.Create("anna@example.com", "hashed", UserRole.Customer, Guid.NewGuid());
        _users.FindByEmailAsync("anna@example.com", Arg.Any<CancellationToken>()).Returns(user);
        _passwords.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        await Should.ThrowAsync<AuthenticationFailedException>(
            () => _service.LoginAsync(new LoginRequest("anna@example.com", "nope"), Ct));
        _tokens.DidNotReceive().Create(Arg.Any<User>());
    }

    [Fact]
    public async Task LoginAsync_UnknownEmail_Throws()
    {
        await Should.ThrowAsync<AuthenticationFailedException>(
            () => _service.LoginAsync(new LoginRequest("nobody@example.com", "whatever"), Ct));
    }
}

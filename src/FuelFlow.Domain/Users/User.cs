using FuelFlow.Domain.Common;

namespace FuelFlow.Domain.Users;

public enum UserRole
{
    Customer,
    Dispatcher,
    Admin,
}

public sealed class User
{
    private User()
    {
    }

    public Guid Id { get; private set; }
    public string Email { get; private set; } = "";

    /// <summary>Produced by ASP.NET Core's PasswordHasher in Infrastructure; the Domain never sees plain passwords.</summary>
    public string PasswordHash { get; private set; } = "";

    public UserRole Role { get; private set; }
    public Guid? CustomerId { get; private set; }

    public static User Create(string? email, string? passwordHash, UserRole role, Guid? customerId = null)
    {
        if (role == UserRole.Customer && customerId is null)
        {
            throw new DomainValidationException("A Customer user must be linked to a customer profile.");
        }

        if (role != UserRole.Customer && customerId is not null)
        {
            throw new DomainValidationException("Only Customer users can be linked to a customer profile.");
        }

        return new User
        {
            Id = Guid.CreateVersion7(),
            Email = Guard.NotBlank(email, "Email").ToLowerInvariant(),
            PasswordHash = Guard.NotBlank(passwordHash, "Password hash"),
            Role = role,
            CustomerId = customerId,
        };
    }
}

using FuelFlow.Application.Auth;
using FuelFlow.Domain.Users;
using Microsoft.AspNetCore.Identity;

namespace FuelFlow.Infrastructure.Auth;

/// <summary>Thin adapter over ASP.NET Core's PasswordHasher (PBKDF2, salted, versioned). We use it without full ASP.NET Identity.</summary>
internal sealed class PasswordService(IPasswordHasher<User> hasher) : IPasswordService
{
    // PasswordHasher<T> never reads the user object, so null is fine here.
    public string Hash(string password) => hasher.HashPassword(null!, password);

    public bool Verify(string passwordHash, string password) =>
        hasher.VerifyHashedPassword(null!, passwordHash, password) != PasswordVerificationResult.Failed;
}

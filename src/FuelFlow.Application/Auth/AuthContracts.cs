using FuelFlow.Domain.Customers;

namespace FuelFlow.Application.Auth;

// DTOs are C# records: immutable, with value equality (like Java records).

public sealed record RegisterRequest(
    string Email, string Password, CustomerType Type, string Phone, string? FullName, string? CompanyName, string? Cvr);

public sealed record RegisterResponse(Guid UserId, Guid CustomerId);

public sealed record LoginRequest(string Email, string Password);

public sealed record LoginResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt);

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

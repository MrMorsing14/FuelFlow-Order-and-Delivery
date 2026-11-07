using FuelFlow.Domain.Common;

namespace FuelFlow.Application.Auth;

/// <summary>Wrong email or password. Deliberately one message for both, so the API does not reveal which emails exist (maps to 401).</summary>
public sealed class AuthenticationFailedException() : Exception("Invalid email or password.");

public sealed class EmailAlreadyRegisteredException() : ConflictDomainException("An account with this email already exists.");

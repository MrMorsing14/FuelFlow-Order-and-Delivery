namespace FuelFlow.Infrastructure.Seeding;

/// <summary>
/// Credentials for the development seed users. They live only in appsettings.Development.json or user secrets,
/// never in committed production config. If a credential is missing, that user is not seeded.
/// </summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public SeedCredentials? Admin { get; set; }
    public SeedCredentials? Dispatcher { get; set; }
    public SeedCredentials? PrivateCustomer { get; set; }
    public SeedCredentials? BusinessCustomer { get; set; }
}

public sealed class SeedCredentials
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";

    public bool IsComplete => !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Password);
}

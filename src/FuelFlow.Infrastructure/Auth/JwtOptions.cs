namespace FuelFlow.Infrastructure.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public const int MinimumKeyBytes = 32;

    /// <summary>HMAC-SHA256 secret. Must come from configuration (user secrets / env var Jwt__SigningKey).</summary>
    public string SigningKey { get; set; } = "";

    public string Issuer { get; set; } = "fuelflow";
    public string Audience { get; set; } = "fuelflow";
    public int ExpiryMinutes { get; set; } = 60;
}

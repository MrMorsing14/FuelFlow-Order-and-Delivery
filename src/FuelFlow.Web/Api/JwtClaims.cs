using System.Security.Claims;
using System.Text.Json;

namespace FuelFlow.Web.Api;

/// <summary>
/// Reads the claims out of a JWT so the UI knows who is logged in and which pages to show.
/// It does NOT check the signature, and cannot: only the server holds the key. The API checks every token on every request,
/// so a forged token here changes what the UI shows but never what the API allows.
/// </summary>
public static class JwtClaims
{
    public static ClaimsPrincipal Anonymous => new(new ClaimsIdentity());

    public static ClaimsPrincipal ToPrincipal(string? token, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return Anonymous;
        }

        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
            {
                return Anonymous;
            }

            using var payload = JsonDocument.Parse(Decode(parts[1]));
            var root = payload.RootElement;
            if (!root.TryGetProperty("exp", out var exp) || DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64()) <= now)
            {
                return Anonymous;
            }

            var claims = new List<Claim>();
            foreach (var name in new[] { "sub", "email", "role", "customer_id" })
            {
                if (root.TryGetProperty(name, out var value) && value.GetString() is { } text)
                {
                    claims.Add(new Claim(name, text));
                }
            }

            return new ClaimsPrincipal(new ClaimsIdentity(claims, "jwt", nameType: "email", roleType: "role"));
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException or ArgumentException)
        {
            return Anonymous;
        }
    }

    // JWTs use base64url (- and _ instead of + and /, no padding); Convert.FromBase64String needs standard base64.
    private static byte[] Decode(string base64Url)
    {
        var base64 = base64Url.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '='));
    }
}

using System.Text;
using FuelFlow.Application.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FuelFlow.Infrastructure.Auth;

internal static class JwtServiceCollectionExtensions
{
    public static IServiceCollection AddJwtAuth(this IServiceCollection services)
    {
        // ValidateOnStart: the app refuses to boot with a missing or too-short signing key.
        services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.SectionName)
            .Validate(
                o => Encoding.UTF8.GetByteCount(o.SigningKey) >= JwtOptions.MinimumKeyBytes,
                $"Jwt:SigningKey is missing or shorter than {JwtOptions.MinimumKeyBytes} bytes.")
            .ValidateOnStart();

        services.AddSingleton<ITokenService, JwtTokenService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>, TimeProvider>((bearer, jwtOptions, timeProvider) =>
            {
                var jwt = jwtOptions.Value;
                bearer.MapInboundClaims = false; // keep the short claim names ("sub", "role") as they are in the token
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    NameClaimType = "sub",
                    RoleClaimType = "role",

                    // Check expiry against the injected TimeProvider (not the system clock) so tests can control time.
                    // Tokens have no "not before" on purpose: with several replicas a slightly slower clock would reject fresh tokens.
                    LifetimeValidator = (_, expires, _, _) => expires is not null && timeProvider.GetUtcNow().UtcDateTime < expires.Value,
                };
            });

        return services;
    }
}

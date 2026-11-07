using System.Security.Claims;
using FuelFlow.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FuelFlow.Api.Controllers;

public sealed record MeResponse(string UserId, string Email, string Role, string? CustomerId);

[ApiController]
[Route("api/auth")]
public sealed class AuthController(AuthService auth) : ControllerBase
{
    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType<RegisterResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken) =>
        StatusCode(StatusCodes.Status201Created, await auth.RegisterAsync(request, cancellationToken));

    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<LoginResponse> Login(LoginRequest request, CancellationToken cancellationToken) =>
        await auth.LoginAsync(request, cancellationToken);

    /// <summary>Who am I? Reads the identity from the JWT claims; handy for clients and for checking a token.</summary>
    [HttpGet("me")]
    [Authorize]
    public MeResponse Me() => new(
        User.FindFirstValue("sub")!,
        User.FindFirstValue("email")!,
        User.FindFirstValue("role")!,
        User.FindFirstValue("customer_id"));
}

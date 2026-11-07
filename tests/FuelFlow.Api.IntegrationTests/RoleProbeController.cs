using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Api.IntegrationTests;

/// <summary>Test-only endpoints so role enforcement can be tested before the real role-protected endpoints exist.</summary>
[ApiController]
[Route("test")]
public sealed class RoleProbeController : ControllerBase
{
    [HttpGet("boom")]
    public IActionResult Boom() => throw new InvalidOperationException("secret internal detail");

    [HttpGet("admin-only")]
    [Authorize(Roles = "Admin")]
    public IActionResult AdminOnly() => Ok();
}

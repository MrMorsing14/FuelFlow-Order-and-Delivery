using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Api.IntegrationTests;

/// <summary>Test-only endpoint that throws, to check what the client sees for an unexpected error.</summary>
[ApiController]
[Route("test")]
public sealed class FailureProbeController : ControllerBase
{
    [HttpGet("boom")]
    public IActionResult Boom() => throw new InvalidOperationException("secret internal detail");
}

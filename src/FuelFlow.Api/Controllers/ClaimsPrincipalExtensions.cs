using System.Security.Claims;

namespace FuelFlow.Api.Controllers;

public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// The logged-in customer's id, read from the validated JWT. Only call this on [Authorize(Roles = "Customer")]
    /// endpoints: customer tokens always carry it. Never take a customer id from the request body or URL.
    /// </summary>
    public static Guid CustomerId(this ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue("customer_id")!);
}

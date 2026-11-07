namespace FuelFlow.Domain.Common;

/// <summary>
/// Base class for business-rule violations. Like a custom RuntimeException in Java.
/// The API layer maps subclasses to HTTP status codes via a global exception handler.
/// </summary>
public class DomainException(string message) : Exception(message);

/// <summary>An input or invariant is invalid (maps to 400 Bad Request).</summary>
public sealed class DomainValidationException(string message) : DomainException(message);

/// <summary>A state change is not allowed from the current state (maps to 409 Conflict).</summary>
public sealed class InvalidOrderTransitionException(Guid orderId, Orders.OrderStatus from, Orders.OrderStatus to)
    : DomainException($"Order {orderId} cannot move from {from} to {to}.")
{
    public Orders.OrderStatus From { get; } = from;
    public Orders.OrderStatus To { get; } = to;
}

/// <summary>A delivery window does not have enough remaining capacity (maps to 409 Conflict).</summary>
public sealed class DeliveryWindowFullException(Guid windowId, int requested, int remaining)
    : DomainException($"Delivery window {windowId} has {remaining} L remaining, {requested} L requested.");

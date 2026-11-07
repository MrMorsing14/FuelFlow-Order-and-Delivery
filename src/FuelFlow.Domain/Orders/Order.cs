using FuelFlow.Domain.Common;
using FuelFlow.Domain.Customers;
using FuelFlow.Domain.Pricing;
using FuelFlow.Domain.Products;

namespace FuelFlow.Domain.Orders;

/// <summary>
/// Order aggregate with a state machine. State changes only happen through the transition methods.
/// Every transition takes "now" as a parameter; the caller gets it from the injected TimeProvider.
/// </summary>
public sealed class Order
{
    private static readonly Dictionary<OrderStatus, OrderStatus[]> _allowedTransitions = new()
    {
        [OrderStatus.Pending] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
        [OrderStatus.Confirmed] = [OrderStatus.Scheduled, OrderStatus.Cancelled],
        [OrderStatus.Scheduled] = [OrderStatus.OutForDelivery, OrderStatus.Cancelled],
        [OrderStatus.OutForDelivery] = [OrderStatus.Delivered],
        [OrderStatus.Delivered] = [],
        [OrderStatus.Cancelled] = [],
    };

    private Order()
    {
    }

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid DeliveryAddressId { get; private set; }
    public Guid ProductId { get; private set; }
    public int OrderedLitres { get; private set; }

    /// <summary>Price snapshot taken at order time. Never changes.</summary>
    public PriceBreakdown Price { get; private set; } = null!;

    /// <summary>Totals recalculated from delivered litres. Set when the order is delivered.</summary>
    public PriceBreakdown? FinalPrice { get; private set; }

    public OrderStatus Status { get; private set; }
    public Guid? DeliveryWindowId { get; private set; }
    public int? DeliveredLitres { get; private set; }
    public string? CancellationReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public DateTimeOffset? ScheduledAt { get; private set; }
    public DateTimeOffset? OutForDeliveryAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    public static Order Place(
        Customer customer, DeliveryAddress address, Product product, int litres, PriceCalculator calculator, DateTimeOffset now)
    {
        if (address.CustomerId != customer.Id)
        {
            throw new DomainValidationException("The delivery address does not belong to the customer.");
        }

        calculator.EnsureValidQuantity(litres, address.TankCapacityLitres);
        var price = calculator.Calculate(product.PriceAt(now), litres, customer.Type);

        return new Order
        {
            Id = Guid.CreateVersion7(),
            CustomerId = customer.Id,
            DeliveryAddressId = address.Id,
            ProductId = product.Id,
            OrderedLitres = litres,
            Price = price,
            Status = OrderStatus.Pending,
            CreatedAt = now,
        };
    }

    public void Confirm(DateTimeOffset now)
    {
        MoveTo(OrderStatus.Confirmed);
        ConfirmedAt = now;
    }

    public void Schedule(Guid deliveryWindowId, DateTimeOffset now)
    {
        if (deliveryWindowId == Guid.Empty)
        {
            throw new DomainValidationException("A delivery window is required.");
        }

        MoveTo(OrderStatus.Scheduled);
        DeliveryWindowId = deliveryWindowId;
        ScheduledAt = now;
    }

    public void StartDelivery(DateTimeOffset now)
    {
        MoveTo(OrderStatus.OutForDelivery);
        OutForDeliveryAt = now;
    }

    /// <summary>Delivered litres may differ from ordered litres by at most 10 %.</summary>
    public void Deliver(int deliveredLitres, DateTimeOffset now)
    {
        EnsureCanMoveTo(OrderStatus.Delivered);

        if (deliveredLitres <= 0 || Math.Abs((long)deliveredLitres - OrderedLitres) * 10 > OrderedLitres)
        {
            throw new DomainValidationException(
                $"Delivered litres must be within 10 % of the ordered {OrderedLitres} litres.");
        }

        MoveTo(OrderStatus.Delivered);
        DeliveredLitres = deliveredLitres;
        FinalPrice = PriceCalculator.Recalculate(Price, deliveredLitres);
        DeliveredAt = now;
    }

    public void Cancel(string? reason, DateTimeOffset now)
    {
        EnsureCanMoveTo(OrderStatus.Cancelled);
        var trimmed = Guard.NotBlank(reason, "Cancellation reason");

        MoveTo(OrderStatus.Cancelled);
        CancellationReason = trimmed;
        CancelledAt = now;
    }

    private void EnsureCanMoveTo(OrderStatus next)
    {
        if (!_allowedTransitions[Status].Contains(next))
        {
            throw new InvalidOrderTransitionException(Id, Status, next);
        }
    }

    private void MoveTo(OrderStatus next)
    {
        EnsureCanMoveTo(next);
        Status = next;
    }
}

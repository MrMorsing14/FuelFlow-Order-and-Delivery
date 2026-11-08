using FuelFlow.Application.Customers;
using FuelFlow.Application.Orders;
using FuelFlow.Domain.Customers;
using FuelFlow.Domain.Orders;
using FuelFlow.Domain.Scheduling;

namespace FuelFlow.Application.Dispatch;

// ---- requests (all filters are optional)

/// <param name="Date">The planned delivery date (the date of the order's delivery window).</param>
public sealed record DispatchOrderFilter(OrderStatus? Status, string? Region, DateOnly? Date);

/// <param name="Reason">Required when delivering more than 10 % less than ordered.</param>
public sealed record DeliverOrderRequest(int DeliveredLitres, string? Reason = null);

public sealed record CreateDeliveryWindowRequest(DateOnly Date, string Region, int CapacityLitres);

/// <param name="From">Defaults to today.</param>
public sealed record DeliveryWindowFilter(DateOnly? From, DateOnly? To, string? Region);

// ---- responses

public sealed record DispatchOrderDto(
    OrderDto Order, string CustomerName, string CustomerPhone, AddressDto Address, string? Region, DateOnly? DeliveryDate);

public sealed record RegionDto(string Name, int PostalCodeFrom, int PostalCodeTo);

public sealed record DeliveryWindowDto(Guid Id, DateOnly Date, string Region, int CapacityLitres, int BookedLitres, int RemainingLitres);

// ---- what the repositories hand back

/// <summary>Region is a name; the repository turns it into the postal code range to filter on.</summary>
public sealed record DispatchOrderQuery(OrderStatus? Status, RegionRange? Region, DateOnly? Date);

public sealed record DispatchOrderRow(Order Order, Customer Customer, DeliveryAddress Address, DateOnly? DeliveryDate);

public sealed record ConfirmedOrder(Guid OrderId, int Litres, string PostalCode, DateTimeOffset CreatedAt);

public interface IDeliveryWindowRepository
{
    Task<DeliveryWindow?> GetAsync(Guid windowId, CancellationToken cancellationToken);

    Task<IReadOnlyList<DeliveryWindow>> ListAsync(DateOnly from, DateOnly? to, string? region, CancellationToken cancellationToken);

    /// <summary>Windows on or after <paramref name="fromDate"/> that still have room.</summary>
    Task<IReadOnlyList<DeliveryWindow>> ListOpenAsync(DateOnly fromDate, CancellationToken cancellationToken);

    void Add(DeliveryWindow window);
}

public static class DispatchMappings
{
    public static DeliveryWindowDto ToDto(this DeliveryWindow w) =>
        new(w.Id, w.Date, w.Region, w.CapacityLitres, w.BookedLitres, w.RemainingLitres);
}

using FluentValidation;
using FuelFlow.Application.Auth;
using FuelFlow.Application.Common;
using FuelFlow.Application.Customers;
using FuelFlow.Application.Dispatch;
using FuelFlow.Application.Products;
using FuelFlow.Domain.Customers;
using FuelFlow.Domain.Orders;
using FuelFlow.Domain.Pricing;
using FuelFlow.Domain.Products;

namespace FuelFlow.Application.Orders;

/// <summary>Used for both quotes and orders: what, where, and how much.</summary>
public sealed record OrderRequest(Guid ProductId, Guid DeliveryAddressId, int Litres);

public sealed record CancelOrderRequest(string Reason);

// shortcut: Price and FinalPrice expose the Domain's PriceBreakdown record directly. Introduce a separate DTO if the API contract must diverge from it.
public sealed record OrderDto(
    Guid Id,
    Guid ProductId,
    Guid DeliveryAddressId,
    int OrderedLitres,
    OrderStatus Status,
    PriceBreakdown Price,
    PriceBreakdown? FinalPrice,
    Guid? DeliveryWindowId,
    int? DeliveredLitres,
    string? CancellationReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? ScheduledAt,
    DateTimeOffset? OutForDeliveryAt,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? CancelledAt);

public interface IOrderRepository
{
    void Add(Order order);

    /// <summary>Returns null if the order does not exist OR belongs to another customer.</summary>
    Task<Order?> GetAsync(Guid orderId, Guid customerId, CancellationToken cancellationToken);

    /// <summary>One customer's orders, newest first.</summary>
    Task<PagedResult<Order>> PageAsync(Guid customerId, PageRequest page, CancellationToken cancellationToken);

    /// <summary>Any customer's order (for dispatchers).</summary>
    Task<Order?> GetAnyAsync(Guid orderId, CancellationToken cancellationToken);

    /// <summary>All customers' orders for the dispatcher list, oldest first.</summary>
    Task<PagedResult<DispatchOrderRow>> PageForDispatchAsync(DispatchOrderQuery query, PageRequest page, CancellationToken cancellationToken);

    /// <summary>Confirmed orders still waiting for a delivery window, with the postal code to deliver to.</summary>
    Task<IReadOnlyList<ConfirmedOrder>> ListConfirmedAsync(CancellationToken cancellationToken);
}

public static class OrderMappings
{
    public static OrderDto ToDto(this Order o) => new(
        o.Id, o.ProductId, o.DeliveryAddressId, o.OrderedLitres, o.Status, o.Price, o.FinalPrice, o.DeliveryWindowId,
        o.DeliveredLitres, o.CancellationReason, o.CreatedAt, o.ConfirmedAt, o.ScheduledAt, o.OutForDeliveryAt, o.DeliveredAt, o.CancelledAt);
}

public sealed class OrderRequestValidator : AbstractValidator<OrderRequest>
{
    // Minimum order and tank capacity depend on pricing options and the address, so PriceCalculator enforces them.
    public OrderRequestValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.DeliveryAddressId).NotEmpty();
        RuleFor(x => x.Litres).GreaterThan(0);
    }
}

public sealed class CancelOrderRequestValidator : AbstractValidator<CancelOrderRequest>
{
    public CancelOrderRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
}

public sealed class OrderService(
    IOrderRepository orders,
    ICustomerRepository customers,
    IAddressRepository addresses,
    IProductRepository products,
    IDeliveryWindowRepository windows,
    IUnitOfWork unitOfWork,
    PriceCalculator calculator,
    TimeProvider timeProvider,
    IValidator<OrderRequest> orderValidator,
    IValidator<CancelOrderRequest> cancelValidator,
    IValidator<PageRequest> pageValidator)
{
    /// <summary>Price for an order that would be placed now. Creates nothing.</summary>
    public async Task<PriceBreakdown> QuoteAsync(Guid customerId, OrderRequest request, CancellationToken ct)
    {
        await orderValidator.ValidateAndThrowAsync(request, ct);
        var (customer, address, product) = await LoadAsync(customerId, request, ct);

        calculator.EnsureValidQuantity(request.Litres, address.TankCapacityLitres);
        return calculator.Calculate(product.PriceAt(timeProvider.GetUtcNow()), request.Litres, customer.Type);
    }

    public async Task<OrderDto> PlaceAsync(Guid customerId, OrderRequest request, CancellationToken ct)
    {
        await orderValidator.ValidateAndThrowAsync(request, ct);
        var (customer, address, product) = await LoadAsync(customerId, request, ct);

        var order = Order.Place(customer, address, product, request.Litres, calculator, timeProvider.GetUtcNow());
        orders.Add(order);
        await unitOfWork.SaveChangesAsync(ct);
        return order.ToDto();
    }

    public async Task<PagedResult<OrderDto>> ListAsync(Guid customerId, PageRequest page, CancellationToken ct)
    {
        await pageValidator.ValidateAndThrowAsync(page, ct);
        return (await orders.PageAsync(customerId, page, ct)).Map(o => o.ToDto());
    }

    public async Task<OrderDto> GetAsync(Guid customerId, Guid orderId, CancellationToken ct) =>
        (await FindOrderAsync(customerId, orderId, ct)).ToDto();

    public async Task<OrderDto> CancelAsync(Guid customerId, Guid orderId, CancelOrderRequest request, CancellationToken ct)
    {
        await cancelValidator.ValidateAndThrowAsync(request, ct);
        var order = await FindOrderAsync(customerId, orderId, ct);

        order.Cancel(request.Reason, timeProvider.GetUtcNow());

        // A scheduled order holds capacity in its window; give it back. Same save, so both changes happen or neither does.
        if (order.DeliveryWindowId is { } windowId && await windows.GetAsync(windowId, ct) is { } window)
        {
            window.Release(order.OrderedLitres);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return order.ToDto();
    }

    private async Task<Order> FindOrderAsync(Guid customerId, Guid orderId, CancellationToken ct) =>
        await orders.GetAsync(orderId, customerId, ct) ?? throw new NotFoundException("Order");

    private async Task<(Customer Customer, DeliveryAddress Address, Product Product)> LoadAsync(
        Guid customerId, OrderRequest request, CancellationToken ct)
    {
        var customer = await customers.GetAsync(customerId, ct) ?? throw new NotFoundException("Customer");
        var address = await addresses.GetAsync(request.DeliveryAddressId, customerId, ct) ?? throw new NotFoundException("Address");
        var product = await products.GetAsync(request.ProductId, ct) ?? throw new NotFoundException("Product");
        return (customer, address, product);
    }
}

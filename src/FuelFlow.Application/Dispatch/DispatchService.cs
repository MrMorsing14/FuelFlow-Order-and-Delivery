using FluentValidation;
using FluentValidation.Results;
using FuelFlow.Application.Auth;
using FuelFlow.Application.Common;
using FuelFlow.Application.Customers;
using FuelFlow.Application.Orders;
using FuelFlow.Domain.Scheduling;

namespace FuelFlow.Application.Dispatch;

public sealed class CreateDeliveryWindowRequestValidator : AbstractValidator<CreateDeliveryWindowRequest>
{
    public CreateDeliveryWindowRequestValidator(RegionMap regions, TimeProvider timeProvider)
    {
        RuleFor(x => x.Date)
            .Must(date => date >= DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime))
            .WithMessage("A delivery window cannot be in the past.");
        RuleFor(x => x.Region)
            .Must(region => regions.Find(region) is not null)
            .WithMessage(_ => $"Unknown region. Known regions: {string.Join(", ", regions.Regions.Select(r => r.Name))}.");
        RuleFor(x => x.CapacityLitres).GreaterThan(0);
    }
}

public sealed class DeliverOrderRequestValidator : AbstractValidator<DeliverOrderRequest>
{
    public DeliverOrderRequestValidator() => RuleFor(x => x.DeliveredLitres).GreaterThan(0);
}

/// <summary>Everything the dispatcher (or admin) does: see all orders, move them along, manage delivery windows.</summary>
public sealed class DispatchService(
    IOrderRepository orders,
    IDeliveryWindowRepository windows,
    IUnitOfWork unitOfWork,
    RegionMap regions,
    TimeProvider timeProvider,
    IValidator<PageRequest> pageValidator,
    IValidator<DeliverOrderRequest> deliverValidator,
    IValidator<CreateDeliveryWindowRequest> windowValidator)
{
    public async Task<PagedResult<DispatchOrderDto>> ListOrdersAsync(DispatchOrderFilter filter, PageRequest page, CancellationToken ct)
    {
        await pageValidator.ValidateAndThrowAsync(page, ct);
        var region = FindRegionOrFail(filter.Region);

        var rows = await orders.PageForDispatchAsync(new DispatchOrderQuery(filter.Status, region, filter.Date), page, ct);
        return rows.Map(r => new DispatchOrderDto(
            r.Order.ToDto(),
            r.Customer.FullName ?? r.Customer.CompanyName ?? r.Customer.Email,
            r.Customer.Phone,
            r.Address.ToDto(),
            regions.RegionOf(r.Address.PostalCode),
            r.DeliveryDate));
    }

    public IReadOnlyList<RegionDto> ListRegions() => [.. regions.Regions.Select(r => new RegionDto(r.Name, r.From, r.To))];

    public Task<OrderDto> ConfirmAsync(Guid orderId, CancellationToken ct) =>
        ChangeAsync(orderId, (order, now) => order.Confirm(now), ct);

    public Task<OrderDto> StartDeliveryAsync(Guid orderId, CancellationToken ct) =>
        ChangeAsync(orderId, (order, now) => order.StartDelivery(now), ct);

    public async Task<OrderDto> DeliverAsync(Guid orderId, DeliverOrderRequest request, CancellationToken ct)
    {
        await deliverValidator.ValidateAndThrowAsync(request, ct);
        return await ChangeAsync(orderId, (order, now) => order.Deliver(request.DeliveredLitres, now), ct);
    }

    public async Task<IReadOnlyList<DeliveryWindowDto>> ListWindowsAsync(DeliveryWindowFilter filter, CancellationToken ct)
    {
        var from = filter.From ?? DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var region = FindRegionOrFail(filter.Region);

        // shortcut: not paged. Windows are one per region per day, so a date range keeps this small; add paging if ranges get wide.
        return [.. (await windows.ListAsync(from, filter.To, region?.Name, ct)).Select(w => w.ToDto())];
    }

    public async Task<DeliveryWindowDto> CreateWindowAsync(CreateDeliveryWindowRequest request, CancellationToken ct)
    {
        await windowValidator.ValidateAndThrowAsync(request, ct);
        var window = DeliveryWindow.Create(request.Date, regions.Find(request.Region)!.Name, request.CapacityLitres);

        windows.Add(window);
        await unitOfWork.SaveChangesAsync(ct);
        return window.ToDto();
    }

    private async Task<OrderDto> ChangeAsync(Guid orderId, Action<Domain.Orders.Order, DateTimeOffset> change, CancellationToken ct)
    {
        var order = await orders.GetAnyAsync(orderId, ct) ?? throw new NotFoundException("Order");

        change(order, timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(ct);
        return order.ToDto();
    }

    private RegionRange? FindRegionOrFail(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return regions.Find(name) ?? throw new ValidationException([new ValidationFailure("region", "Unknown region.")]);
    }
}

using FuelFlow.Application.Auth;
using FuelFlow.Application.Common;
using FuelFlow.Application.Orders;
using FuelFlow.Domain.Common;
using FuelFlow.Domain.Customers;
using FuelFlow.Domain.Orders;
using FuelFlow.Domain.Scheduling;

namespace FuelFlow.Application.Dispatch;

public sealed record SchedulingResult(int Scheduled, int Skipped);

/// <summary>
/// Runs <see cref="DeliveryScheduler"/> against the database: plan first, then apply each assignment on its own.
/// Safe to run on several replicas at once: the database concurrency checks let only one of them win each booking.
/// </summary>
public sealed class SchedulingService(
    IOrderRepository orders,
    IDeliveryWindowRepository windows,
    IUnitOfWork unitOfWork,
    RegionMap regions,
    TimeProvider timeProvider)
{
    public async Task<SchedulingResult> RunAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var confirmed = await orders.ListConfirmedAsync(ct);
        if (confirmed.Count == 0)
        {
            return new SchedulingResult(0, 0);
        }

        var earliest = DateOnly.FromDateTime(now.UtcDateTime).AddDays(DeliveryScheduler.MinimumLeadDays);
        var plan = DeliveryScheduler.Plan(
            confirmed.Select(o => new SchedulableOrder(o.OrderId, o.Litres, regions.RegionOf(PostalCode.Create(o.PostalCode)), o.CreatedAt)),
            await windows.ListOpenAsync(earliest, ct),
            now);

        var scheduled = 0;
        foreach (var assignment in plan)
        {
            scheduled += await ApplyAsync(assignment, ct) ? 1 : 0;
        }

        return new SchedulingResult(scheduled, confirmed.Count - scheduled);
    }

    /// <summary>
    /// Books the window and schedules the order in one save. Returns false (and changes nothing) if the plan is out of date:
    /// the order was cancelled meanwhile, the window filled up, or another process got there first.
    /// </summary>
    public async Task<bool> ApplyAsync(Assignment assignment, CancellationToken ct)
    {
        try
        {
            var order = await orders.GetAnyAsync(assignment.OrderId, ct);
            var window = await windows.GetAsync(assignment.WindowId, ct);
            if (order is null || window is null)
            {
                return false;
            }

            window.Book(order.OrderedLitres);
            order.Schedule(window.Id, timeProvider.GetUtcNow());
            await unitOfWork.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is ConcurrencyConflictException or DeliveryWindowFullException or InvalidOrderTransitionException)
        {
            unitOfWork.DiscardChanges();
            return false;
        }
    }
}

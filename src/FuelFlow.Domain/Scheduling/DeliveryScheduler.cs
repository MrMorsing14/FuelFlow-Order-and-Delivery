namespace FuelFlow.Domain.Scheduling;

/// <summary>What the scheduler needs to know about a confirmed order.</summary>
public sealed record SchedulableOrder(Guid OrderId, int Litres, string? Region, DateTimeOffset CreatedAt);

/// <summary>"Put this order into that window."</summary>
public sealed record Assignment(Guid OrderId, Guid WindowId);

/// <summary>
/// Decides which confirmed order goes into which delivery window. Pure: no database, no clock, nothing is changed.
/// It only returns a plan; applying the plan (booking capacity, scheduling orders) is the caller's job.
/// </summary>
public static class DeliveryScheduler
{
    /// <summary>A window must be at least this many calendar days after today (UTC). Same-day delivery is never planned.</summary>
    public const int MinimumLeadDays = 1;

    /// <summary>
    /// Oldest orders first. Each order gets the earliest window in its region that still has enough room,
    /// counting the litres already planned in this run. An order that fits nowhere is left out (it is retried
    /// next time) and does not block later, smaller orders.
    /// </summary>
    public static IReadOnlyList<Assignment> Plan(
        IEnumerable<SchedulableOrder> orders, IEnumerable<DeliveryWindow> windows, DateTimeOffset now)
    {
        var earliestDate = DateOnly.FromDateTime(now.UtcDateTime).AddDays(MinimumLeadDays);
        var slots = windows
            .Where(w => w.Date >= earliestDate)
            .OrderBy(w => w.Date).ThenBy(w => w.Id)
            .Select(w => new Slot(w.Id, w.Region, w.RemainingLitres))
            .ToList();

        var plan = new List<Assignment>();
        foreach (var order in orders.Where(o => o.Region is not null).OrderBy(o => o.CreatedAt).ThenBy(o => o.OrderId))
        {
            var slot = slots.FirstOrDefault(s => s.Region == order.Region && s.RemainingLitres >= order.Litres);
            if (slot is null)
            {
                continue;
            }

            slot.RemainingLitres -= order.Litres;
            plan.Add(new Assignment(order.OrderId, slot.WindowId));
        }

        return plan;
    }

    private sealed class Slot(Guid windowId, string region, int remainingLitres)
    {
        public Guid WindowId { get; } = windowId;
        public string Region { get; } = region;
        public int RemainingLitres { get; set; } = remainingLitres;
    }
}

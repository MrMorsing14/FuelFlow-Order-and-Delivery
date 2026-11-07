using FuelFlow.Domain.Scheduling;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace FuelFlow.Domain.UnitTests;

public class DeliverySchedulerTests
{
    // Monday 2 March 2026, 08:00 UTC
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 2, 8, 0, 0, TimeSpan.Zero));

    private static readonly DateOnly _today = new(2026, 3, 2);

    private static DeliveryWindow Window(int daysFromToday, string region = "Copenhagen", int capacity = 10_000, int booked = 0)
    {
        var window = DeliveryWindow.Create(_today.AddDays(daysFromToday), region, capacity);
        if (booked > 0)
        {
            window.Book(booked);
        }

        return window;
    }

    private static SchedulableOrder Order(int litres = 1000, string? region = "Copenhagen", int createdMinute = 0) =>
        new(Guid.NewGuid(), litres, region, new DateTimeOffset(2026, 3, 1, 12, createdMinute, 0, TimeSpan.Zero));

    private IReadOnlyList<Assignment> Plan(IEnumerable<SchedulableOrder> orders, params DeliveryWindow[] windows) =>
        DeliveryScheduler.Plan(orders, windows, _time.GetUtcNow());

    [Fact]
    public void Plan_SeveralWindows_PicksTheEarliestOne()
    {
        var later = Window(3);
        var earliest = Window(1);
        var middle = Window(2);
        var order = Order();

        var plan = Plan([order], later, earliest, middle);

        plan.ShouldBe([new Assignment(order.OrderId, earliest.Id)]);
    }

    [Fact]
    public void Plan_WindowTodayAndTomorrow_NeverPlansTodayAndUsesTomorrowAtTheEarliest()
    {
        var today = Window(0);
        var tomorrow = Window(1);
        var order = Order();

        Plan([order], today, tomorrow).Single().WindowId.ShouldBe(tomorrow.Id);
        Plan([order], today).ShouldBeEmpty();
    }

    [Fact]
    public void Plan_LateInTheEvening_StillRequiresTheNextCalendarDay()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 3, 2, 23, 59, 0, TimeSpan.Zero));
        var tomorrow = Window(1);
        var order = Order();

        Plan([order], tomorrow).Single().WindowId.ShouldBe(tomorrow.Id);

        _time.Advance(TimeSpan.FromMinutes(2)); // now 3 March 00:01: tomorrow is no longer allowed
        Plan([order], tomorrow).ShouldBeEmpty();
    }

    [Fact]
    public void Plan_OrderInAnotherRegion_IsNeverPutIntoTheWrongRegionsWindow()
    {
        var copenhagen = Window(1, "Copenhagen");
        var aarhus = Window(2, "Aarhus");
        var order = Order(region: "Aarhus");

        Plan([order], copenhagen, aarhus).Single().WindowId.ShouldBe(aarhus.Id);
        Plan([order], copenhagen).ShouldBeEmpty();
    }

    [Fact]
    public void Plan_FullWindow_IsSkippedForTheNextDay()
    {
        var full = Window(1, capacity: 5000, booked: 4500);
        var next = Window(2, capacity: 5000);
        var order = Order(litres: 600);

        Plan([order], full, next).Single().WindowId.ShouldBe(next.Id);
    }

    [Theory]
    [InlineData(500, true)]  // exactly the remaining capacity
    [InlineData(501, false)]
    public void Plan_RemainingCapacityBoundary_FitsExactlyUpToTheRemainingLitres(int litres, bool fits)
    {
        var window = Window(1, capacity: 5000, booked: 4500);

        Plan([Order(litres)], window).Count.ShouldBe(fits ? 1 : 0);
    }

    [Fact]
    public void Plan_SeveralOrders_CountLitresAlreadyPlannedInThisRun()
    {
        var first = Window(1, capacity: 2500);
        var second = Window(2, capacity: 2500);
        var a = Order(1000, createdMinute: 1);
        var b = Order(1000, createdMinute: 2);
        var c = Order(1000, createdMinute: 3);

        var plan = Plan([a, b, c], first, second);

        plan.ShouldBe([new Assignment(a.OrderId, first.Id), new Assignment(b.OrderId, first.Id), new Assignment(c.OrderId, second.Id)]);
    }

    [Fact]
    public void Plan_NotEnoughRoomForEveryone_OldestOrderGetsThePlaceRegardlessOfInputOrder()
    {
        var window = Window(1, capacity: 1000);
        var older = Order(1000, createdMinute: 1);
        var newer = Order(1000, createdMinute: 2);

        Plan([newer, older], window).ShouldBe([new Assignment(older.OrderId, window.Id)]);
    }

    [Fact]
    public void Plan_OrderTooBigForAnyWindow_DoesNotBlockSmallerLaterOrders()
    {
        var window = Window(1, capacity: 2000);
        var big = Order(3000, createdMinute: 1);
        var small = Order(500, createdMinute: 2);

        Plan([big, small], window).ShouldBe([new Assignment(small.OrderId, window.Id)]);
    }

    [Fact]
    public void Plan_OrderWithoutRegion_IsLeftOut()
    {
        Plan([Order(region: null)], Window(1)).ShouldBeEmpty();
    }

    [Fact]
    public void Plan_SameInputTwice_GivesTheSamePlan()
    {
        var windows = new[] { Window(1, capacity: 1500), Window(1, "Aarhus", 1500), Window(2, capacity: 1500) };
        var orders = new[] { Order(1000, createdMinute: 1), Order(1000, "Aarhus", 1), Order(1000, createdMinute: 1) };

        Plan(orders, windows).ShouldBe(Plan(orders.Reverse(), windows.Reverse().ToArray()));
    }

    [Fact]
    public void Plan_DoesNotChangeTheWindows()
    {
        var window = Window(1, capacity: 5000);

        Plan([Order(1000)], window);

        window.BookedLitres.ShouldBe(0);
    }
}

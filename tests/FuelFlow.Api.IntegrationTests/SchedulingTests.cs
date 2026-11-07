using System.Net;
using System.Net.Http.Json;
using FuelFlow.Api.Scheduling;
using FuelFlow.Application.Common;
using FuelFlow.Application.Dispatch;
using FuelFlow.Application.Orders;
using FuelFlow.Domain.Orders;
using FuelFlow.Domain.Scheduling;
using FuelFlow.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Shouldly;

namespace FuelFlow.Api.IntegrationTests;

public class SchedulingTests(FuelFlowApiFactory factory) : ApiTestBase(factory)
{
    private sealed record Setup(Session Customer, HttpClient Dispatcher, Guid ProductId, Guid AddressId);

    private async Task<Setup> SetupAsync(string postalCode = "2100", int tankCapacityLitres = 20_000)
    {
        var customer = await RegisterCustomerAsync();
        var dispatcher = await StaffClientAsync(UserRole.Dispatcher);
        var productId = await SeedProductAsync();
        var address = await CreateAddressAsync(customer.Client, tankCapacityLitres, postalCode);
        return new Setup(customer, dispatcher, productId, address.Id);
    }

    private static async Task<OrderDto> ConfirmedOrderAsync(Setup s, int litres = 1000)
    {
        var order = await PlaceOrderAsync(s.Customer, s.ProductId, s.AddressId, litres);
        return await ConfirmAsync(s.Dispatcher, order.Id);
    }

    [Fact]
    public async Task Run_ConfirmedOrder_GoesToEarliestWindowInItsRegionOnAtLeastTomorrowAndBooksCapacity()
    {
        var s = await SetupAsync(postalCode: "2100"); // Copenhagen
        await SeedWindowAsync(0, "Copenhagen");                    // today: never used
        await SeedWindowAsync(1, "Aarhus");                        // wrong region
        var expected = await SeedWindowAsync(2, "Copenhagen");
        await SeedWindowAsync(3, "Copenhagen");
        var order = await ConfirmedOrderAsync(s, 1200);
        Factory.Time.Advance(TimeSpan.FromMinutes(5));

        var result = await RunSchedulerAsync();

        result.ShouldBe(new SchedulingResult(1, 0));
        var stored = await LoadOrderAsync(order.Id);
        (stored.Status, stored.DeliveryWindowId).ShouldBe((OrderStatus.Scheduled, expected));
        stored.ScheduledAt.ShouldBe(FuelFlowApiFactory.StartTime.AddMinutes(5));
        (await LoadWindowAsync(expected)).BookedLitres.ShouldBe(1200);
    }

    [Fact]
    public async Task Run_NoWindowAvailable_OrderStaysConfirmedAndIsScheduledOnceAWindowExists()
    {
        var s = await SetupAsync();
        var order = await ConfirmedOrderAsync(s);

        (await RunSchedulerAsync()).ShouldBe(new SchedulingResult(0, 1));
        (await LoadOrderAsync(order.Id)).Status.ShouldBe(OrderStatus.Confirmed);

        await SeedWindowAsync(1);
        (await RunSchedulerAsync()).Scheduled.ShouldBe(1);
        (await LoadOrderAsync(order.Id)).Status.ShouldBe(OrderStatus.Scheduled);
    }

    [Fact]
    public async Task Run_FullWindow_IsSkippedAndTheNextDayIsUsed()
    {
        var s = await SetupAsync();
        var small = await SeedWindowAsync(1, capacityLitres: 1500);
        var next = await SeedWindowAsync(2, capacityLitres: 5000);
        var first = await ConfirmedOrderAsync(s, 1000);
        var second = await ConfirmedOrderAsync(s, 1000);

        await RunSchedulerAsync();

        (await LoadOrderAsync(first.Id)).DeliveryWindowId.ShouldBe(small);
        (await LoadOrderAsync(second.Id)).DeliveryWindowId.ShouldBe(next);
        (await LoadWindowAsync(small)).RemainingLitres.ShouldBe(500);
    }

    [Fact]
    public async Task Run_PendingAndCancelledOrders_AreIgnored()
    {
        var s = await SetupAsync();
        var windowId = await SeedWindowAsync(1);
        var pending = await PlaceOrderAsync(s.Customer, s.ProductId, s.AddressId);
        var cancelled = await ConfirmedOrderAsync(s);
        (await s.Customer.Client.PostAsJsonAsync($"/api/orders/{cancelled.Id}/cancel", new { reason = "no" }, Ct)).EnsureSuccessStatusCode();

        (await RunSchedulerAsync()).ShouldBe(new SchedulingResult(0, 0));

        (await LoadOrderAsync(pending.Id)).Status.ShouldBe(OrderStatus.Pending);
        (await LoadWindowAsync(windowId)).BookedLitres.ShouldBe(0);
    }

    [Fact]
    public async Task Run_RunTwice_DoesNotScheduleOrBookTheSameOrderAgain()
    {
        var s = await SetupAsync();
        var windowId = await SeedWindowAsync(1);
        await ConfirmedOrderAsync(s, 1000);

        await RunSchedulerAsync();
        (await RunSchedulerAsync()).ShouldBe(new SchedulingResult(0, 0));

        (await LoadWindowAsync(windowId)).BookedLitres.ShouldBe(1000);
    }

    [Fact]
    public async Task Cancel_ScheduledOrder_ReleasesWindowCapacity()
    {
        var s = await SetupAsync();
        var windowId = await SeedWindowAsync(1);
        var keep = await ConfirmedOrderAsync(s, 1000);
        var cancel = await ConfirmedOrderAsync(s, 700);
        await RunSchedulerAsync();
        (await LoadWindowAsync(windowId)).BookedLitres.ShouldBe(1700);

        var response = await s.Customer.Client.PostAsJsonAsync($"/api/orders/{cancel.Id}/cancel", new { reason = "Changed my mind" }, Ct);

        response.EnsureSuccessStatusCode();
        (await LoadWindowAsync(windowId)).BookedLitres.ShouldBe(1000);
        (await LoadOrderAsync(cancel.Id)).Status.ShouldBe(OrderStatus.Cancelled);
        (await LoadOrderAsync(keep.Id)).Status.ShouldBe(OrderStatus.Scheduled);
    }

    [Fact]
    public async Task Apply_TwoOrdersBookTheSameNearlyFullWindowAtTheSameTime_OnlyOneSucceeds()
    {
        var s = await SetupAsync();
        var windowId = await SeedWindowAsync(1, capacityLitres: 1000);
        var a = await ConfirmedOrderAsync(s, 600);
        var b = await ConfirmedOrderAsync(s, 600);

        async Task<bool> Apply(Guid orderId)
        {
            await using var scope = NewScope(); // its own DbContext, like a separate request or replica
            return await scope.ServiceProvider.GetRequiredService<SchedulingService>().ApplyAsync(new Assignment(orderId, windowId), Ct);
        }

        var results = await Task.WhenAll(Task.Run(() => Apply(a.Id), Ct), Task.Run(() => Apply(b.Id), Ct));

        results.Count(applied => applied).ShouldBe(1);
        (await LoadWindowAsync(windowId)).BookedLitres.ShouldBe(600);
        var statuses = new[] { (await LoadOrderAsync(a.Id)).Status, (await LoadOrderAsync(b.Id)).Status };
        statuses.Count(status => status == OrderStatus.Scheduled).ShouldBe(1);
        statuses.Count(status => status == OrderStatus.Confirmed).ShouldBe(1);
    }

    [Fact]
    public async Task Run_SeveralRunsAtTheSameTime_NeverBookMoreThanTheOrdersThatWereScheduled()
    {
        var s = await SetupAsync();
        var windowId = await SeedWindowAsync(1, capacityLitres: 10_000);
        var orders = new List<OrderDto>();
        for (var i = 0; i < 5; i++)
        {
            orders.Add(await ConfirmedOrderAsync(s, 1000));
        }

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(RunSchedulerAsync, Ct)));

        var scheduled = new List<Order>();
        foreach (var order in orders)
        {
            var stored = await LoadOrderAsync(order.Id);
            if (stored.Status == OrderStatus.Scheduled)
            {
                scheduled.Add(stored);
            }
        }

        // However the runs interleaved, the window holds exactly the litres of the orders that were scheduled into it.
        (await LoadWindowAsync(windowId)).BookedLitres.ShouldBe(scheduled.Sum(o => o.OrderedLitres));

        // Whatever a lost race left over is picked up by the next run, and nothing is booked twice.
        await RunSchedulerAsync();
        (await LoadWindowAsync(windowId)).BookedLitres.ShouldBe(5000);
    }

    [Fact]
    public async Task Order_CancelledAndConfirmedAtTheSameTime_TheSecondSaveGetsAConflict()
    {
        var s = await SetupAsync();
        var order = await PlaceOrderAsync(s.Customer, s.ProductId, s.AddressId);
        await using var customerSide = NewScope();
        await using var dispatcherSide = NewScope();
        var forCancel = await Db(customerSide).Orders.SingleAsync(o => o.Id == order.Id, Ct);
        var forConfirm = await Db(dispatcherSide).Orders.SingleAsync(o => o.Id == order.Id, Ct);

        forCancel.Cancel("Changed my mind", Factory.Time.GetUtcNow());
        forConfirm.Confirm(Factory.Time.GetUtcNow());
        await Db(customerSide).SaveChangesAsync(Ct);

        await Should.ThrowAsync<ConcurrencyConflictException>(() => Db(dispatcherSide).SaveChangesAsync(Ct));
        (await LoadOrderAsync(order.Id)).Status.ShouldBe(OrderStatus.Cancelled);
    }

    [Fact]
    public async Task Worker_AfterTheIntervalPasses_SchedulesConfirmedOrders()
    {
        var s = await SetupAsync();
        var windowId = await SeedWindowAsync(1);
        var order = await ConfirmedOrderAsync(s);
        var workerClock = new FakeTimeProvider();
        using var worker = new SchedulingWorker(
            Factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new SchedulingOptions { IntervalMinutes = 5 }),
            workerClock,
            NullLogger<SchedulingWorker>.Instance);
        await worker.StartAsync(Ct);

        try
        {
            await Task.Delay(100, Ct); // let the loop reach its first wait
            (await LoadOrderAsync(order.Id)).Status.ShouldBe(OrderStatus.Confirmed, "nothing runs before the interval has passed");

            workerClock.Advance(TimeSpan.FromMinutes(5));

            await WaitUntilAsync(async () => (await LoadOrderAsync(order.Id)).Status == OrderStatus.Scheduled);
            (await LoadWindowAsync(windowId)).BookedLitres.ShouldBe(1000);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Worker_IntervalZero_IsOffAndNeverSchedules()
    {
        var s = await SetupAsync();
        await SeedWindowAsync(1);
        var order = await ConfirmedOrderAsync(s);
        var workerClock = new FakeTimeProvider();
        using var worker = new SchedulingWorker(
            Factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new SchedulingOptions { IntervalMinutes = 0 }),
            workerClock,
            NullLogger<SchedulingWorker>.Instance);

        await worker.StartAsync(Ct);
        workerClock.Advance(TimeSpan.FromHours(1));
        await Task.Delay(200, Ct);
        await worker.StopAsync(CancellationToken.None);

        (await LoadOrderAsync(order.Id)).Status.ShouldBe(OrderStatus.Confirmed);
    }

    [Fact]
    public async Task Worker_OrdersStillWaitingForAWindow_LogsAWarning()
    {
        var s = await SetupAsync();
        await ConfirmedOrderAsync(s); // no window exists, so it cannot be scheduled
        var logger = new ListLogger<SchedulingWorker>();
        using var worker = new SchedulingWorker(
            Factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new SchedulingOptions { IntervalMinutes = 5 }), new FakeTimeProvider(), logger);

        await worker.RunOnceAsync(Ct);

        logger.Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Message.Contains("1 confirmed order(s) are still waiting"));
    }

    [Fact]
    public async Task Worker_ARunFails_IsLoggedAndDoesNotThrowSoTheLoopKeepsGoing()
    {
        var scopes = Substitute.For<IServiceScopeFactory>();
        scopes.CreateScope().Returns(_ => throw new InvalidOperationException("database is down"));
        using var worker = new SchedulingWorker(
            scopes, Options.Create(new SchedulingOptions { IntervalMinutes = 5 }), new FakeTimeProvider(), NullLogger<SchedulingWorker>.Instance);

        await Should.NotThrowAsync(() => worker.RunOnceAsync(Ct));
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition was not met in time.");
            }

            await Task.Delay(50, Ct);
        }
    }
}

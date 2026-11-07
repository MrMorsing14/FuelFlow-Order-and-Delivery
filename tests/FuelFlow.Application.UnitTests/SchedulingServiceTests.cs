using FuelFlow.Application.Auth;
using FuelFlow.Application.Common;
using FuelFlow.Application.Dispatch;
using FuelFlow.Application.Orders;
using FuelFlow.Domain.Common;
using FuelFlow.Domain.Customers;
using FuelFlow.Domain.Orders;
using FuelFlow.Domain.Pricing;
using FuelFlow.Domain.Products;
using FuelFlow.Domain.Scheduling;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Shouldly;

namespace FuelFlow.Application.UnitTests;

public class SchedulingServiceTests
{
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IDeliveryWindowRepository _windows = Substitute.For<IDeliveryWindowRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 2, 8, 0, 0, TimeSpan.Zero));
    private readonly Customer _customer = Customer.CreatePrivate("Anna Hansen", "+4512345678", "anna@example.com");
    private readonly SchedulingService _service;

    public SchedulingServiceTests()
    {
        var regions = new RegionMap([new RegionRange { Name = "Copenhagen", From = 1000, To = 2999 }]);
        _service = new SchedulingService(_orders, _windows, _unitOfWork, regions, _time);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Order ConfirmedOrder(int litres = 1000)
    {
        var address = DeliveryAddress.Create(_customer.Id, "Vejen 1", "2100", "København Ø", 10_000);
        var product = Product.Create(ProductType.Diesel, 10m, _time.GetUtcNow().AddDays(-1));
        var order = Order.Place(_customer, address, product, litres, new PriceCalculator(new PricingOptions()), _time.GetUtcNow());
        order.Confirm(_time.GetUtcNow());
        _orders.GetAnyAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        return order;
    }

    private DeliveryWindow Window(int daysAhead = 1, int capacity = 5000)
    {
        var window = DeliveryWindow.Create(DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime).AddDays(daysAhead), "Copenhagen", capacity);
        _windows.GetAsync(window.Id, Arg.Any<CancellationToken>()).Returns(window);
        return window;
    }

    [Fact]
    public async Task ApplyAsync_ValidAssignment_BooksWindowSchedulesOrderAndSavesOnce()
    {
        var order = ConfirmedOrder(1000);
        var window = Window();

        var applied = await _service.ApplyAsync(new Assignment(order.Id, window.Id), Ct);

        applied.ShouldBeTrue();
        window.BookedLitres.ShouldBe(1000);
        order.Status.ShouldBe(OrderStatus.Scheduled);
        order.DeliveryWindowId.ShouldBe(window.Id);
        await _unitOfWork.Received(1).SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task ApplyAsync_SaveLosesTheConcurrencyRace_DiscardsChangesAndReturnsFalse()
    {
        var order = ConfirmedOrder();
        var window = Window();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns<Task<int>>(_ => throw new ConcurrencyConflictException());

        var applied = await _service.ApplyAsync(new Assignment(order.Id, window.Id), Ct);

        applied.ShouldBeFalse();
        _unitOfWork.Received(1).DiscardChanges();
    }

    [Fact]
    public async Task ApplyAsync_WindowFilledUpMeanwhile_ReturnsFalseAndSavesNothing()
    {
        var order = ConfirmedOrder(1000);
        var window = Window(capacity: 1500);
        window.Book(1000); // someone else took the room after the plan was made

        var applied = await _service.ApplyAsync(new Assignment(order.Id, window.Id), Ct);

        applied.ShouldBeFalse();
        order.Status.ShouldBe(OrderStatus.Confirmed);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        _unitOfWork.Received(1).DiscardChanges();
    }

    [Fact]
    public async Task ApplyAsync_OrderCancelledMeanwhile_ReturnsFalseAndDoesNotBookCapacity()
    {
        var order = ConfirmedOrder();
        order.Cancel("Changed my mind", _time.GetUtcNow());
        var window = Window();

        var applied = await _service.ApplyAsync(new Assignment(order.Id, window.Id), Ct);

        applied.ShouldBeFalse();
        // Booking happens before scheduling, but nothing was saved and the changes were discarded.
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        _unitOfWork.Received(1).DiscardChanges();
    }

    [Fact]
    public async Task ApplyAsync_UnknownOrderOrWindow_ReturnsFalse()
    {
        (await _service.ApplyAsync(new Assignment(Guid.NewGuid(), Guid.NewGuid()), Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_NothingConfirmed_DoesNotEvenLoadWindows()
    {
        _orders.ListConfirmedAsync(Arg.Any<CancellationToken>()).Returns([]);

        var result = await _service.RunAsync(Ct);

        result.ShouldBe(new SchedulingResult(0, 0));
        await _windows.DidNotReceive().ListOpenAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_TwoOrdersOneLosesTheRace_ContinuesWithTheRestAndCountsBoth()
    {
        var first = ConfirmedOrder(1000);
        var second = ConfirmedOrder(1000);
        var window = Window(capacity: 5000);
        _orders.ListConfirmedAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new ConfirmedOrder(first.Id, 1000, "2100", first.CreatedAt),
            new ConfirmedOrder(second.Id, 1000, "2100", first.CreatedAt.AddMinutes(1)),
        ]);
        _windows.ListOpenAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([window]);
        var saves = 0;
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            ++saves == 1 ? throw new ConcurrencyConflictException() : Task.FromResult(1));

        var result = await _service.RunAsync(Ct);

        result.ShouldBe(new SchedulingResult(1, 1));
        _unitOfWork.Received(1).DiscardChanges();
    }

    [Fact]
    public async Task RunAsync_OrderOutsideAnyRegion_IsNotScheduled()
    {
        var order = ConfirmedOrder();
        _orders.ListConfirmedAsync(Arg.Any<CancellationToken>()).Returns([new ConfirmedOrder(order.Id, 1000, "9000", order.CreatedAt)]);
        var window = Window();
        _windows.ListOpenAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([window]);

        var result = await _service.RunAsync(Ct);

        result.ShouldBe(new SchedulingResult(0, 1));
    }
}

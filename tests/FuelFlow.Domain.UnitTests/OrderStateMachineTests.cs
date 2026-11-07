using FuelFlow.Domain.Common;
using FuelFlow.Domain.Orders;
using Shouldly;

namespace FuelFlow.Domain.UnitTests;

public class OrderStateMachineTests
{
    public enum OrderAction
    {
        Confirm,
        Schedule,
        StartDelivery,
        Deliver,
        Cancel,
    }

    // Every status x every action. The expected result is spelled out here, independent of the Order code.
    public static TheoryData<OrderStatus, OrderAction, bool> AllCombinations()
    {
        var data = new TheoryData<OrderStatus, OrderAction, bool>();
        foreach (var status in Enum.GetValues<OrderStatus>())
        {
            foreach (var action in Enum.GetValues<OrderAction>())
            {
                data.Add(status, action, IsLegal(status, action));
            }
        }

        return data;
    }

    private static bool IsLegal(OrderStatus status, OrderAction action) => (status, action) switch
    {
        (OrderStatus.Pending, OrderAction.Confirm) => true,
        (OrderStatus.Confirmed, OrderAction.Schedule) => true,
        (OrderStatus.Scheduled, OrderAction.StartDelivery) => true,
        (OrderStatus.OutForDelivery, OrderAction.Deliver) => true,
        (OrderStatus.Pending or OrderStatus.Confirmed or OrderStatus.Scheduled, OrderAction.Cancel) => true,
        _ => false,
    };

    private static void Apply(Order order, OrderAction action)
    {
        switch (action)
        {
            case OrderAction.Confirm: order.Confirm(TestData.Now); break;
            case OrderAction.Schedule: order.Schedule(Guid.NewGuid(), TestData.Now); break;
            case OrderAction.StartDelivery: order.StartDelivery(TestData.Now); break;
            case OrderAction.Deliver: order.Deliver(1000, TestData.Now); break;
            case OrderAction.Cancel: order.Cancel("Customer changed their mind", TestData.Now); break;
            default: throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    [Theory]
    [MemberData(nameof(AllCombinations))]
    public void Transition_AllStatusActionCombinations_OnlyLegalOnesSucceed(OrderStatus from, OrderAction action, bool legal)
    {
        var order = TestData.OrderAt(from);

        if (legal)
        {
            Should.NotThrow(() => Apply(order, action));
        }
        else
        {
            var ex = Should.Throw<InvalidOrderTransitionException>(() => Apply(order, action));
            ex.From.ShouldBe(from);
            order.Status.ShouldBe(from, "a rejected transition must not change the status");
        }
    }

    [Fact]
    public void Place_ValidInput_StartsPendingWithCreatedAt()
    {
        var order = TestData.PlaceOrder();

        order.Status.ShouldBe(OrderStatus.Pending);
        order.CreatedAt.ShouldBe(TestData.Now);
        order.OrderedLitres.ShouldBe(1000);
        order.Price.TotalIncVat.ShouldBe(12_500m);
    }

    [Fact]
    public void Transitions_FullHappyPath_RecordsEachTimestamp()
    {
        var window = Guid.NewGuid();
        var order = TestData.PlaceOrder();

        order.Confirm(TestData.Now.AddHours(1));
        order.Schedule(window, TestData.Now.AddHours(2));
        order.StartDelivery(TestData.Now.AddDays(2));
        order.Deliver(1000, TestData.Now.AddDays(2).AddHours(3));

        order.Status.ShouldBe(OrderStatus.Delivered);
        order.DeliveryWindowId.ShouldBe(window);
        order.ConfirmedAt.ShouldBe(TestData.Now.AddHours(1));
        order.ScheduledAt.ShouldBe(TestData.Now.AddHours(2));
        order.OutForDeliveryAt.ShouldBe(TestData.Now.AddDays(2));
        order.DeliveredAt.ShouldBe(TestData.Now.AddDays(2).AddHours(3));
        order.CancelledAt.ShouldBeNull();
    }

    [Fact]
    public void Cancel_PendingOrder_StoresReasonAndTimestamp()
    {
        var order = TestData.PlaceOrder();

        order.Cancel("  Wrong address  ", TestData.Now.AddMinutes(5));

        order.Status.ShouldBe(OrderStatus.Cancelled);
        order.CancellationReason.ShouldBe("Wrong address");
        order.CancelledAt.ShouldBe(TestData.Now.AddMinutes(5));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Cancel_BlankReason_ThrowsAndKeepsStatus(string? reason)
    {
        var order = TestData.PlaceOrder();

        Should.Throw<DomainValidationException>(() => order.Cancel(reason, TestData.Now));
        order.Status.ShouldBe(OrderStatus.Pending);
    }

    [Fact]
    public void Schedule_EmptyWindowId_Throws()
    {
        var order = TestData.OrderAt(OrderStatus.Confirmed);

        Should.Throw<DomainValidationException>(() => order.Schedule(Guid.Empty, TestData.Now));
        order.Status.ShouldBe(OrderStatus.Confirmed);
    }
}

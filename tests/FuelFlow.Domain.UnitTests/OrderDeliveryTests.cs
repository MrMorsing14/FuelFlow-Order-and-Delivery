using FuelFlow.Domain.Common;
using FuelFlow.Domain.Orders;
using Shouldly;

namespace FuelFlow.Domain.UnitTests;

public class OrderDeliveryTests
{
    [Theory]
    [InlineData(900)]
    [InlineData(950)]
    [InlineData(1000)]
    [InlineData(1050)]
    [InlineData(1100)]
    public void Deliver_WithinTenPercent_Succeeds(int delivered)
    {
        var order = TestData.OrderAt(OrderStatus.OutForDelivery, litres: 1000);

        order.Deliver(delivered, TestData.Now);

        order.Status.ShouldBe(OrderStatus.Delivered);
        order.DeliveredLitres.ShouldBe(delivered);
    }

    [Theory]
    [InlineData(1101)]
    [InlineData(int.MaxValue)] // used to overflow the check and slip through
    [InlineData(0)]
    [InlineData(-100)]
    public void Deliver_MoreThanTenPercentOverOrNonPositive_ThrowsEvenWithAReason(int delivered)
    {
        var order = TestData.OrderAt(OrderStatus.OutForDelivery, litres: 1000);

        Should.Throw<DomainValidationException>(() => order.Deliver(delivered, TestData.Now, "truck was out of product"));

        order.Status.ShouldBe(OrderStatus.OutForDelivery);
        order.DeliveredLitres.ShouldBeNull();
        order.FinalPrice.ShouldBeNull();
    }

    [Theory]
    [InlineData(899, null)]
    [InlineData(1, null)]
    [InlineData(500, "")]
    [InlineData(500, "   ")]
    public void Deliver_MoreThanTenPercentUnderWithoutAReason_ThrowsAndStaysOutForDelivery(int delivered, string? reason)
    {
        var order = TestData.OrderAt(OrderStatus.OutForDelivery, litres: 1000);

        Should.Throw<DomainValidationException>(() => order.Deliver(delivered, TestData.Now, reason));

        order.Status.ShouldBe(OrderStatus.OutForDelivery);
        order.DeliveredLitres.ShouldBeNull();
        order.ShortDeliveryReason.ShouldBeNull();
        order.FinalPrice.ShouldBeNull();
    }

    [Theory]
    [InlineData(899)]
    [InlineData(1)]
    public void Deliver_MoreThanTenPercentUnderWithAReason_SucceedsAndKeepsTheTrimmedReason(int delivered)
    {
        var order = TestData.OrderAt(OrderStatus.OutForDelivery, litres: 1000);

        order.Deliver(delivered, TestData.Now, "  truck was out of product ");

        order.Status.ShouldBe(OrderStatus.Delivered);
        order.DeliveredLitres.ShouldBe(delivered);
        order.ShortDeliveryReason.ShouldBe("truck was out of product");
        order.FinalPrice.ShouldNotBeNull();
    }

    [Fact]
    public void Deliver_WithinTenPercentAndAReasonIsGiven_DoesNotStoreTheReason()
    {
        var order = TestData.OrderAt(OrderStatus.OutForDelivery, litres: 1000);

        order.Deliver(900, TestData.Now, "not needed");

        order.ShortDeliveryReason.ShouldBeNull();
    }

    [Fact]
    public void Deliver_FewerLitres_RecalculatesFinalPriceFromSnapshotUnitPrice()
    {
        var order = TestData.PlaceOrder(litres: 1000, unitPrice: 10m);
        order.Confirm(TestData.Now);
        order.Schedule(Guid.NewGuid(), TestData.Now);
        order.StartDelivery(TestData.Now);

        order.Deliver(950, TestData.Now);

        order.Price.TotalIncVat.ShouldBe(12_500m, "the order-time snapshot is never changed");
        order.FinalPrice.ShouldNotBeNull();
        order.FinalPrice.NetAmount.ShouldBe(9_500m);
        order.FinalPrice.VatAmount.ShouldBe(2_375m);
        order.FinalPrice.TotalIncVat.ShouldBe(11_875m);
    }

    [Fact]
    public void Deliver_BusinessOrderWithDiscount_KeepsDiscountOnFinalPrice()
    {
        var customer = TestData.BusinessCustomer();
        var order = TestData.PlaceOrder(customer, litres: 5000);
        order.Confirm(TestData.Now);
        order.Schedule(Guid.NewGuid(), TestData.Now);
        order.StartDelivery(TestData.Now);

        order.Deliver(4600, TestData.Now);

        order.FinalPrice!.DiscountAmount.ShouldBe(920m);
        order.FinalPrice.TotalExVat.ShouldBe(45_080m);
        order.FinalPrice.DisplayIncVat.ShouldBeFalse();
    }

    [Fact]
    public void Place_UnitPriceChangedLater_OrderKeepsPriceFromOrderTime()
    {
        var customer = TestData.PrivateCustomer();
        var product = TestData.Product(10m);
        product.ChangePrice(12m, TestData.Now.AddDays(1), TestData.Now);

        var order = Domain.Orders.Order.Place(
            customer, TestData.AddressFor(customer), product, 1000, TestData.Calculator(), TestData.Now);

        order.Price.UnitPriceExVat.ShouldBe(10m);
    }
}

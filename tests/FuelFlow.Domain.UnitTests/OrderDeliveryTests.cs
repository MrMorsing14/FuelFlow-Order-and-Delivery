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
    [InlineData(899)]
    [InlineData(1101)]
    [InlineData(0)]
    [InlineData(-100)]
    public void Deliver_OutsideTenPercentOrNonPositive_ThrowsAndStaysOutForDelivery(int delivered)
    {
        var order = TestData.OrderAt(OrderStatus.OutForDelivery, litres: 1000);

        Should.Throw<DomainValidationException>(() => order.Deliver(delivered, TestData.Now));

        order.Status.ShouldBe(OrderStatus.OutForDelivery);
        order.DeliveredLitres.ShouldBeNull();
        order.FinalPrice.ShouldBeNull();
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
        product.ChangePrice(12m, TestData.Now.AddDays(1));

        var order = Domain.Orders.Order.Place(
            customer, TestData.AddressFor(customer), product, 1000, TestData.Calculator(), TestData.Now);

        order.Price.UnitPriceExVat.ShouldBe(10m);
    }
}

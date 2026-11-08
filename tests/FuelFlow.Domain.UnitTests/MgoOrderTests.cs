using FuelFlow.Domain.Common;
using FuelFlow.Domain.Customers;
using FuelFlow.Domain.Orders;
using FuelFlow.Domain.Products;
using Shouldly;

namespace FuelFlow.Domain.UnitTests;

public class MgoOrderTests
{
    private static Product Mgo() => Product.Create(ProductType.Mgo, 9.90m, TestData.Now.AddDays(-30));

    private static Order PlaceMgo(Customer customer, int litres, int tank = 200_000) =>
        Order.Place(customer, TestData.AddressFor(customer, tank), Mgo(), litres, TestData.Calculator(), TestData.Now);

    [Fact]
    public void Place_PrivateCustomer_IsRefused()
    {
        var anna = TestData.PrivateCustomer();

        var ex = Should.Throw<DomainValidationException>(() => PlaceMgo(anna, 20_000));

        ex.Message.ShouldContain("business customers");
    }

    [Fact]
    public void Place_BusinessCustomer_Succeeds()
    {
        var order = PlaceMgo(TestData.BusinessCustomer(), 20_000);

        order.OrderedLitres.ShouldBe(20_000);
        order.Status.ShouldBe(OrderStatus.Pending);
    }

    [Fact]
    public void RequiresBusinessCustomer_OnlyMgo()
    {
        Mgo().RequiresBusinessCustomer.ShouldBeTrue();
        TestData.Product().RequiresBusinessCustomer.ShouldBeFalse();
    }

    [Fact]
    public void Place_BelowTheMgoMinimum_IsRefused()
    {
        var ex = Should.Throw<DomainValidationException>(() => PlaceMgo(TestData.BusinessCustomer(), 9_999));

        ex.Message.ShouldBe("Minimum order of MGO is 10.000 litres.");
    }

    [Theory]
    [InlineData(10_000)]
    [InlineData(42_000)] // exactly one full truck
    [InlineData(52_000)] // one full truck plus a second truck with the 10,000 minimum
    [InlineData(60_000)]
    [InlineData(84_000)] // two full trucks
    [InlineData(94_000)]
    public void Place_ValidTruckLoads_Succeed(int litres)
    {
        Should.NotThrow(() => PlaceMgo(TestData.BusinessCustomer(), litres));
    }

    [Theory]
    [InlineData(42_001)]
    [InlineData(45_000)]
    [InlineData(51_999)]
    [InlineData(84_001)]
    [InlineData(93_999)]
    public void Place_LastTruckBelowTheMinimum_IsRefused(int litres)
    {
        Should.Throw<DomainValidationException>(() => PlaceMgo(TestData.BusinessCustomer(), litres));
    }

    [Fact]
    public void Place_ExtraTruckForTooLittle_ExplainsAndNamesTheNextValidAmounts()
    {
        var ex = Should.Throw<DomainValidationException>(() => PlaceMgo(TestData.BusinessCustomer(), 45_000));

        ex.Message.ShouldBe(
            "We can't accommodate 45.000 litres of MGO. One truck carries 42.000 litres, " +
            "and we can't send another truck for just 3.000 litres extra: it must carry at least 10.000 litres. " +
            "Order 42.000 litres or less, or 52.000 litres or more.");
    }

    [Fact]
    public void Place_ThirdTruckForTooLittle_NamesTheTwoTruckAmount()
    {
        var ex = Should.Throw<DomainValidationException>(() => PlaceMgo(TestData.BusinessCustomer(), 85_000));

        ex.Message.ShouldContain("Order 84.000 litres, or 94.000 litres or more.");
    }

    [Fact]
    public void Place_MoreThanTheTank_IsStillRefused()
    {
        Should.Throw<DomainValidationException>(() => PlaceMgo(TestData.BusinessCustomer(), 52_000, tank: 50_000));
    }

    [Fact]
    public void Place_OtherProducts_AreNotAffectedByTheMgoRules()
    {
        var anna = TestData.PrivateCustomer();

        Should.NotThrow(() => TestData.PlaceOrder(anna, litres: 1000));
        Should.NotThrow(() => Order.Place(anna, TestData.AddressFor(anna, 100_000), TestData.Product(), 45_000, TestData.Calculator(), TestData.Now));
    }
}

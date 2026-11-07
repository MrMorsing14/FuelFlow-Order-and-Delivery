using FuelFlow.Domain.Common;
using FuelFlow.Domain.Orders;
using Shouldly;

namespace FuelFlow.Domain.UnitTests;

public class OrderPlacementValidationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(499)]
    public void Place_BelowMinimumOrder_Throws(int litres)
    {
        var customer = TestData.PrivateCustomer();

        Should.Throw<DomainValidationException>(() => Order.Place(
            customer, TestData.AddressFor(customer), TestData.Product(), litres, TestData.Calculator(), TestData.Now));
    }

    [Fact]
    public void Place_ExactlyMinimumOrder_Succeeds()
    {
        var customer = TestData.PrivateCustomer();

        var order = Order.Place(
            customer, TestData.AddressFor(customer), TestData.Product(), 500, TestData.Calculator(), TestData.Now);

        order.OrderedLitres.ShouldBe(500);
        order.Price.SmallOrderFee.ShouldBe(250m);
    }

    [Theory]
    [InlineData(3000, true)]
    [InlineData(3001, false)]
    public void Place_AgainstTankCapacity_RejectsOverCapacity(int litres, bool valid)
    {
        var customer = TestData.PrivateCustomer();
        var address = TestData.AddressFor(customer, tankCapacityLitres: 3000);
        var act = () => Order.Place(customer, address, TestData.Product(), litres, TestData.Calculator(), TestData.Now);

        if (valid)
        {
            Should.NotThrow(act);
        }
        else
        {
            Should.Throw<DomainValidationException>(act);
        }
    }

    [Fact]
    public void Place_AddressOfAnotherCustomer_Throws()
    {
        var customer = TestData.PrivateCustomer();
        var otherAddress = TestData.AddressFor(TestData.BusinessCustomer());

        Should.Throw<DomainValidationException>(() => Order.Place(
            customer, otherAddress, TestData.Product(), 1000, TestData.Calculator(), TestData.Now));
    }

    [Fact]
    public void Place_NoPriceValidYet_Throws()
    {
        var customer = TestData.PrivateCustomer();
        var product = Domain.Products.Product.Create(Domain.Products.ProductType.Diesel, 10m, TestData.Now.AddDays(1));

        Should.Throw<DomainValidationException>(() => Order.Place(
            customer, TestData.AddressFor(customer), product, 1000, TestData.Calculator(), TestData.Now));
    }
}

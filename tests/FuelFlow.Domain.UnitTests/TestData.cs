using FuelFlow.Domain.Customers;
using FuelFlow.Domain.Orders;
using FuelFlow.Domain.Pricing;
using FuelFlow.Domain.Products;

namespace FuelFlow.Domain.UnitTests;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

    // Valid CVR numbers (pass the modulus-11 check).
    public const string ValidCvr = "10150817";
    public const string OtherValidCvr = "13585628";

    public static PriceCalculator Calculator() => new(new PricingOptions());

    public static Customer PrivateCustomer() =>
        Customer.CreatePrivate("Anna Hansen", "+4512345678", "anna@example.com");

    public static Customer BusinessCustomer() =>
        Customer.CreateBusiness("Hansen Transport ApS", ValidCvr, "+4587654321", "kontor@hansen.example");

    public static DeliveryAddress AddressFor(Customer customer, int tankCapacityLitres = 10_000) =>
        DeliveryAddress.Create(customer.Id, "Vejen 1", "2100", "København Ø", tankCapacityLitres);

    public static Product Product(decimal pricePerLitre = 10m) =>
        Products.Product.Create(ProductType.HeatingOil, pricePerLitre, Now.AddDays(-30));

    public static Order PlaceOrder(Customer? customer = null, int litres = 1000, decimal unitPrice = 10m)
    {
        customer ??= PrivateCustomer();
        return Order.Place(customer, AddressFor(customer), Product(unitPrice), litres, Calculator(), Now);
    }

    /// <summary>Builds an order that has walked the legal path to the given status.</summary>
    public static Order OrderAt(OrderStatus status, int litres = 1000)
    {
        var order = PlaceOrder(litres: litres);
        if (status == OrderStatus.Cancelled)
        {
            order.Cancel("test", Now);
            return order;
        }

        if (status >= OrderStatus.Confirmed)
        {
            order.Confirm(Now);
        }

        if (status >= OrderStatus.Scheduled)
        {
            order.Schedule(Guid.NewGuid(), Now);
        }

        if (status >= OrderStatus.OutForDelivery)
        {
            order.StartDelivery(Now);
        }

        if (status >= OrderStatus.Delivered)
        {
            order.Deliver(litres, Now);
        }

        return order;
    }
}

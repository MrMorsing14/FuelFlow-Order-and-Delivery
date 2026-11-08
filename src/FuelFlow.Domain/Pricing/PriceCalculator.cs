using FuelFlow.Domain.Common;
using System.Globalization;
using FuelFlow.Domain.Customers;
using FuelFlow.Domain.Products;

namespace FuelFlow.Domain.Pricing;

/// <summary>The single place where prices are calculated.</summary>
public sealed class PriceCalculator(PricingOptions options)
{
    // Same formatting as the UI (1.000), without depending on the machine's culture data.
    private static readonly NumberFormatInfo _danish = new() { NumberGroupSeparator = "." };

    /// <summary>
    /// Everything that decides whether this customer may order this much of this product to this address.
    /// Used for both quotes and orders, so they always agree.
    /// MGO has three extra rules: business customers only, its own minimum, and a truck limit. One truck carries
    /// <see cref="PricingOptions.MgoTruckCapacityLitres"/>; an order above that needs more trucks, and the last
    /// truck must carry at least the MGO minimum or it is not worth sending. With 42,000 per truck and a 10,000 minimum:
    /// 42,001 to 51,999 is refused, 52,000 is fine, 84,001 to 93,999 is refused, and so on.
    /// </summary>
    public void EnsureCanOrder(Customer customer, Product product, int litres, int tankCapacityLitres)
    {
        if (product.RequiresBusinessCustomer && customer.Type != CustomerType.Business)
        {
            throw new DomainValidationException($"{product.Type} can only be ordered by business customers.");
        }

        if (product.Type == ProductType.Mgo)
        {
            EnsureMgoQuantity(litres);
        }

        EnsureValidQuantity(litres, tankCapacityLitres);
    }

    private void EnsureMgoQuantity(int litres)
    {
        var minimum = options.MgoMinimumOrderLitres;
        var truck = options.MgoTruckCapacityLitres;

        if (litres < minimum)
        {
            throw new DomainValidationException($"Minimum order of MGO is {Format(minimum)} litres.");
        }

        var lastTruck = litres % truck;
        if (litres <= truck || lastTruck == 0 || lastTruck >= minimum)
        {
            return;
        }

        var fullTrucks = litres - lastTruck;
        var nextValid = fullTrucks + minimum;
        var accepted = fullTrucks == truck
            ? $"{Format(fullTrucks)} litres or less, or {Format(nextValid)} litres or more"
            : $"{Format(fullTrucks)} litres, or {Format(nextValid)} litres or more";
        throw new DomainValidationException(
            $"We can't accommodate {Format(litres)} litres of MGO. One truck carries {Format(truck)} litres, " +
            $"and we can't send another truck for just {Format(lastTruck)} litres extra: it must carry at least {Format(minimum)} litres. " +
            $"Order {accepted}.");
    }

    private static string Format(int litres) => litres.ToString("N0", _danish);

    /// <summary>Min order and tank capacity. Used for both quotes and orders.</summary>
    public void EnsureValidQuantity(int litres, int tankCapacityLitres)
    {
        if (litres < options.MinimumOrderLitres)
        {
            throw new DomainValidationException($"Minimum order is {options.MinimumOrderLitres} litres.");
        }

        if (litres > tankCapacityLitres)
        {
            throw new DomainValidationException($"Quantity exceeds the tank capacity of {tankCapacityLitres} litres.");
        }
    }

    /// <summary>A unit price including VAT, for display (e.g. the public price list).</summary>
    public decimal AddVat(decimal amountExVat) => Round(amountExVat * (1 + options.VatRate));

    public PriceBreakdown Calculate(decimal unitPriceExVat, int litres, CustomerType customerType)
    {
        Guard.Positive(unitPriceExVat, "Unit price");
        Guard.Positive(litres, "Litres");

        var discountRate = customerType == CustomerType.Business && litres >= options.BusinessDiscountThresholdLitres
            ? options.BusinessDiscountRate
            : 0m;
        var fee = litres < options.SmallOrderThresholdLitres ? options.SmallOrderFee : 0m;

        return Build(unitPriceExVat, litres, discountRate, fee, options.VatRate, displayIncVat: customerType == CustomerType.Private);
    }

    /// <summary>
    /// Recalculates totals for a different quantity (delivered litres) using the order-time snapshot:
    /// same unit price, same discount rate, same fee, same VAT rate. Thresholds are NOT re-evaluated.
    /// Static because it needs no options, only the snapshot.
    /// </summary>
    public static PriceBreakdown Recalculate(PriceBreakdown snapshot, int litres)
    {
        Guard.Positive(litres, "Litres");
        return Build(snapshot.UnitPriceExVat, litres, snapshot.DiscountRate, snapshot.SmallOrderFee, snapshot.VatRate, snapshot.DisplayIncVat);
    }

    private static PriceBreakdown Build(
        decimal unitPrice, int litres, decimal discountRate, decimal fee, decimal vatRate, bool displayIncVat)
    {
        var net = Round(litres * unitPrice);
        var discount = Round(net * discountRate);
        var totalExVat = net - discount + fee;
        var vat = Round(totalExVat * vatRate);

        return new PriceBreakdown(
            litres, unitPrice, net, discountRate, discount, fee, totalExVat, vatRate, vat, totalExVat + vat, displayIncVat);
    }

    // Banker's rounding is the .NET default, so the rounding mode must be explicit.
    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

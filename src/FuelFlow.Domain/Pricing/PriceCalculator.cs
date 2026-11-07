using FuelFlow.Domain.Common;
using FuelFlow.Domain.Customers;

namespace FuelFlow.Domain.Pricing;

/// <summary>The single place where prices are calculated.</summary>
public sealed class PriceCalculator(PricingOptions options)
{
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

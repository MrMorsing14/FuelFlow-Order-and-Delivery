namespace FuelFlow.Domain.Pricing;

/// <summary>
/// Pricing rules. Plain class with defaults; the Api binds it from configuration via IOptions
/// (the Domain itself has no dependencies, so it only knows this POCO).
/// </summary>
public sealed class PricingOptions
{
    public const string SectionName = "Pricing";

    public int MinimumOrderLitres { get; set; } = 500;
    public int SmallOrderThresholdLitres { get; set; } = 1000;
    public decimal SmallOrderFee { get; set; } = 250m;
    public int BusinessDiscountThresholdLitres { get; set; } = 5000;
    public decimal BusinessDiscountRate { get; set; } = 0.02m;
    public decimal VatRate { get; set; } = 0.25m;

    /// <summary>MGO (marine diesel) is for ships: a bigger minimum than the other products.</summary>
    public int MgoMinimumOrderLitres { get; set; } = 10_000;

    /// <summary>One truck carries this much MGO. Bigger orders need more trucks; see <see cref="PriceCalculator.EnsureCanOrder"/>.</summary>
    public int MgoTruckCapacityLitres { get; set; } = 42_000;
}

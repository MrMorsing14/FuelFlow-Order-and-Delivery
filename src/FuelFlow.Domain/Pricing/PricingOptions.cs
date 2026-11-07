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
}

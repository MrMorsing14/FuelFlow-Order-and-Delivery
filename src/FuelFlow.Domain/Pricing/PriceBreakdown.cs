namespace FuelFlow.Domain.Pricing;

/// <summary>
/// Immutable price snapshot (a C# record: value equality, `with` copies). Stored on the order at order time.
/// Contains both ex-VAT and inc-VAT figures; <see cref="DisplayIncVat"/> says which one the UI should show.
/// </summary>
public sealed record PriceBreakdown(
    int Litres,
    decimal UnitPriceExVat,
    decimal NetAmount,
    decimal DiscountRate,
    decimal DiscountAmount,
    decimal SmallOrderFee,
    decimal TotalExVat,
    decimal VatRate,
    decimal VatAmount,
    decimal TotalIncVat,
    bool DisplayIncVat);

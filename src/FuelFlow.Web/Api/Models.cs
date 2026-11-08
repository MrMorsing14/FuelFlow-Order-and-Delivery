namespace FuelFlow.Web.Api;

// What the API sends and expects. Deliberately separate from the server's classes: the UI only depends on the JSON.
// Statuses and types are plain strings ("Pending", "Private", ...), matching the API's string enums.

public sealed record LoginResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt);

public sealed record RegisterRequest(string Email, string Password, string Type, string Phone, string? FullName, string? CompanyName, string? Cvr);

public sealed record Product(Guid Id, string Type, decimal PricePerLitreExVat, decimal PricePerLitreIncVat, bool RequiresBusinessCustomer = false);

public sealed record Customer(Guid Id, string Type);

public sealed record Address(Guid Id, string Street, string PostalCode, string City, int TankCapacityLitres, string? AccessNotes);

public sealed record AddressRequest(string Street, string PostalCode, string City, int TankCapacityLitres, string? AccessNotes);

public sealed record OrderRequest(Guid ProductId, Guid DeliveryAddressId, int Litres);

public sealed record Price(
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
    bool DisplayIncVat)
{
    /// <summary>Private customers see prices including VAT, business customers excluding VAT.</summary>
    public decimal Displayed => DisplayIncVat ? TotalIncVat : TotalExVat;

    public string DisplayedLabel => DisplayIncVat ? "Total incl. VAT" : "Total excl. VAT";
}

public sealed record Order(
    Guid Id,
    Guid ProductId,
    Guid DeliveryAddressId,
    int OrderedLitres,
    string Status,
    Price Price,
    Price? FinalPrice,
    Guid? DeliveryWindowId,
    int? DeliveredLitres,
    string? CancellationReason,
    string? ShortDeliveryReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeliveredAt)
{
    /// <summary>The final price once delivered, otherwise the price from order time.</summary>
    public Price CurrentPrice => FinalPrice ?? Price;

    public bool CanBeCancelled => Status is "Pending" or "Confirmed" or "Scheduled";
}

public sealed record Paged<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

public sealed record DispatchOrder(Order Order, string CustomerName, string CustomerPhone, Address Address, string? Region, DateOnly? DeliveryDate);

public sealed record Region(string Name, int PostalCodeFrom, int PostalCodeTo);

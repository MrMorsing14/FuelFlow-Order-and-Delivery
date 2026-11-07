using FuelFlow.Domain.Common;

namespace FuelFlow.Domain.Products;

public enum ProductType
{
    HeatingOil,
    Diesel,
    Hvo,
}

public sealed class PriceHistory
{
    private PriceHistory()
    {
    }

    public Guid Id { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal PricePerLitreExVat { get; private set; }
    public DateTimeOffset ValidFrom { get; private set; }

    internal static PriceHistory Create(Guid productId, decimal price, DateTimeOffset validFrom) => new()
    {
        Id = Guid.CreateVersion7(),
        ProductId = productId,
        PricePerLitreExVat = Guard.Positive(price, "Price"),
        ValidFrom = validFrom,
    };
}

public sealed class Product
{
    private readonly List<PriceHistory> _priceHistory = [];

    private Product()
    {
    }

    public Guid Id { get; private set; }
    public ProductType Type { get; private set; }

    /// <summary>Append-only: prices are never overwritten, so old orders keep their price.</summary>
    public IReadOnlyList<PriceHistory> PriceHistory => _priceHistory;

    public static Product Create(ProductType type, decimal initialPricePerLitreExVat, DateTimeOffset validFrom)
    {
        var product = new Product { Id = Guid.CreateVersion7(), Type = type };
        product._priceHistory.Add(Products.PriceHistory.Create(product.Id, initialPricePerLitreExVat, validFrom));
        return product;
    }

    public void ChangePrice(decimal newPricePerLitreExVat, DateTimeOffset validFrom)
    {
        var latest = _priceHistory.MaxBy(p => p.ValidFrom)!;
        if (validFrom <= latest.ValidFrom)
        {
            throw new DomainValidationException("A new price must be valid from after the latest existing price.");
        }

        _priceHistory.Add(Products.PriceHistory.Create(Id, newPricePerLitreExVat, validFrom));
    }

    /// <summary>The price in force at the given moment (excl. VAT).</summary>
    public decimal PriceAt(DateTimeOffset at) =>
        _priceHistory.Where(p => p.ValidFrom <= at).MaxBy(p => p.ValidFrom)?.PricePerLitreExVat
        ?? throw new DomainValidationException($"No price for {Type} is valid at {at:O}.");
}

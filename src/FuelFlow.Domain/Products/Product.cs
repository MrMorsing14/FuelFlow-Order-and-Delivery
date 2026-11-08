using FuelFlow.Domain.Common;

namespace FuelFlow.Domain.Products;

public enum ProductType
{
    HeatingOil,
    Diesel,
    Hvo,

    /// <summary>Marine gas oil (marine diesel) for large ships such as ferries. Business customers only.</summary>
    Mgo,
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

    /// <summary>Some products are not sold to private customers. This is the one place that says which.</summary>
    public bool RequiresBusinessCustomer => Type == ProductType.Mgo;

    /// <summary>Append-only: prices are never overwritten, so old orders keep their price.</summary>
    public IReadOnlyList<PriceHistory> PriceHistory => _priceHistory;

    public static Product Create(ProductType type, decimal initialPricePerLitreExVat, DateTimeOffset validFrom)
    {
        var product = new Product { Id = Guid.CreateVersion7(), Type = type };
        product._priceHistory.Add(Products.PriceHistory.Create(product.Id, initialPricePerLitreExVat, validFrom));
        return product;
    }

    /// <summary>
    /// Adds a price valid from <paramref name="validFrom"/>. Prices that are scheduled for the future and not yet in force
    /// (valid from the same time or later) are replaced, so a wrong scheduled price can be corrected. Prices already in force
    /// are never touched, so old orders and the history stay intact.
    /// </summary>
    public void ChangePrice(decimal newPricePerLitreExVat, DateTimeOffset validFrom, DateTimeOffset now)
    {
        var superseded = _priceHistory.Where(p => p.ValidFrom >= validFrom && p.ValidFrom > now).ToList();
        var latest = _priceHistory.Except(superseded).MaxBy(p => p.ValidFrom)!;
        if (validFrom <= latest.ValidFrom)
        {
            throw new DomainValidationException("A new price must be valid from after the latest price already in force.");
        }

        var price = Products.PriceHistory.Create(Id, newPricePerLitreExVat, validFrom);
        superseded.ForEach(p => _priceHistory.Remove(p));
        _priceHistory.Add(price);
    }

    /// <summary>The price in force at the given moment (excl. VAT).</summary>
    public decimal PriceAt(DateTimeOffset at) =>
        _priceHistory.Where(p => p.ValidFrom <= at).MaxBy(p => p.ValidFrom)?.PricePerLitreExVat
        ?? throw new DomainValidationException($"No price for {Type} is valid at {at:O}.");
}

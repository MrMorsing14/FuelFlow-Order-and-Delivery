using FluentValidation;
using FuelFlow.Application.Auth;
using FuelFlow.Application.Common;
using FuelFlow.Domain.Pricing;
using FuelFlow.Domain.Products;

namespace FuelFlow.Application.Products;

public sealed record ProductDto(
    Guid Id, ProductType Type, decimal PricePerLitreExVat, decimal PricePerLitreIncVat, DateTimeOffset PriceValidFrom, bool RequiresBusinessCustomer);

/// <summary>ValidFrom is optional: leave it out to change the price right now, or give a future time to schedule it.</summary>
public sealed record ChangePriceRequest(decimal PricePerLitreExVat, DateTimeOffset? ValidFrom);

public sealed record PriceChangeDto(Guid ProductId, decimal PricePerLitreExVat, DateTimeOffset ValidFrom);

public interface IProductRepository
{
    Task<IReadOnlyList<Product>> ListAsync(CancellationToken cancellationToken);

    Task<Product?> GetAsync(Guid productId, CancellationToken cancellationToken);
}

public sealed class ChangePriceRequestValidator : AbstractValidator<ChangePriceRequest>
{
    public ChangePriceRequestValidator(TimeProvider timeProvider)
    {
        RuleFor(x => x.PricePerLitreExVat).GreaterThan(0);
        RuleFor(x => x.ValidFrom)
            .Must(validFrom => validFrom is null || validFrom >= timeProvider.GetUtcNow())
            .WithMessage("A price cannot be changed retroactively.");
    }
}

public sealed class ProductService(
    IProductRepository products,
    IUnitOfWork unitOfWork,
    PriceCalculator calculator,
    TimeProvider timeProvider,
    IValidator<ChangePriceRequest> validator)
{
    public async Task<IReadOnlyList<ProductDto>> ListAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        return [.. (await products.ListAsync(ct)).Select(p => ToDto(p, now)).OrderBy(p => p.Type)];
    }

    public async Task<PriceChangeDto> ChangePriceAsync(Guid productId, ChangePriceRequest request, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(request, ct);
        var product = await products.GetAsync(productId, ct) ?? throw new NotFoundException("Product");
        var now = timeProvider.GetUtcNow();
        var validFrom = request.ValidFrom ?? now;

        product.ChangePrice(request.PricePerLitreExVat, validFrom, now);
        await unitOfWork.SaveChangesAsync(ct);
        return new PriceChangeDto(product.Id, request.PricePerLitreExVat, validFrom);
    }

    private ProductDto ToDto(Product product, DateTimeOffset now)
    {
        var current = product.PriceHistory.Where(p => p.ValidFrom <= now).MaxBy(p => p.ValidFrom)
            ?? throw new InvalidOperationException($"Product {product.Id} has no price in force.");
        return new ProductDto(product.Id, product.Type, current.PricePerLitreExVat, calculator.AddVat(current.PricePerLitreExVat), current.ValidFrom, product.RequiresBusinessCustomer);
    }
}

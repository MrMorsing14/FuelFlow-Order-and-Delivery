using FuelFlow.Domain.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Infrastructure.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Type).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(p => p.Type).IsUnique();

        // EF fills the private _priceHistory list directly, so the public property can stay read-only.
        builder.HasMany(p => p.PriceHistory).WithOne().HasForeignKey(h => h.ProductId).OnDelete(DeleteBehavior.Cascade);
        // AutoInclude: a Product without its price history cannot price anything, so always load it.
        builder.Navigation(p => p.PriceHistory).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
    }
}

internal sealed class PriceHistoryConfiguration : IEntityTypeConfiguration<PriceHistory>
{
    public void Configure(EntityTypeBuilder<PriceHistory> builder)
    {
        builder.ToTable("price_history");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id).ValueGeneratedNever();
        builder.Property(h => h.PricePerLitreExVat).HasPrecision(18, 4);
        builder.HasIndex(h => new { h.ProductId, h.ValidFrom }).IsUnique();
    }
}

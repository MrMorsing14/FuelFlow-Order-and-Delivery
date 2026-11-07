using FuelFlow.Domain.Customers;
using FuelFlow.Domain.Orders;
using FuelFlow.Domain.Pricing;
using FuelFlow.Domain.Products;
using FuelFlow.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.CancellationReason).HasMaxLength(500);

        // Complex types: the price snapshot is stored as columns on the orders table (Price_NetAmount, ...)
        // rather than a separate table. Final price is optional until the order is delivered.
        builder.ComplexProperty(o => o.Price, ConfigurePrice);
        builder.ComplexProperty(o => o.FinalPrice, ConfigurePrice);

        // No navigation properties: the Order only holds foreign keys to other aggregates.
        builder.HasOne<Customer>().WithMany().HasForeignKey(o => o.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DeliveryAddress>().WithMany().HasForeignKey(o => o.DeliveryAddressId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Product>().WithMany().HasForeignKey(o => o.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DeliveryWindow>().WithMany().HasForeignKey(o => o.DeliveryWindowId).OnDelete(DeleteBehavior.Restrict);

        // Optimistic concurrency, like on delivery windows: a customer cancelling while a dispatcher confirms
        // (or two scheduler runs) cannot both win; the loser gets a 409.
        builder.Property<uint>("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();

        builder.HasIndex(o => new { o.CustomerId, o.CreatedAt });
        builder.HasIndex(o => o.Status);
        builder.HasIndex(o => o.DeliveryWindowId);
    }

    private static void ConfigurePrice(ComplexPropertyBuilder<PriceBreakdown> price)
    {
        price.Property(p => p.UnitPriceExVat).HasPrecision(18, 4);
        price.Property(p => p.NetAmount).HasPrecision(18, 2);
        price.Property(p => p.DiscountRate).HasPrecision(5, 4);
        price.Property(p => p.DiscountAmount).HasPrecision(18, 2);
        price.Property(p => p.SmallOrderFee).HasPrecision(18, 2);
        price.Property(p => p.TotalExVat).HasPrecision(18, 2);
        price.Property(p => p.VatRate).HasPrecision(5, 4);
        price.Property(p => p.VatAmount).HasPrecision(18, 2);
        price.Property(p => p.TotalIncVat).HasPrecision(18, 2);
    }
}

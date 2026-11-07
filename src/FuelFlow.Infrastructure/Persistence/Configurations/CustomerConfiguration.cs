using FuelFlow.Domain.Common;
using FuelFlow.Domain.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Phone).HasMaxLength(50).IsRequired();
        builder.Property(c => c.Email).HasMaxLength(320).IsRequired();
        builder.Property(c => c.FullName).HasMaxLength(200);
        builder.Property(c => c.CompanyName).HasMaxLength(200);

        // Value converter: the CvrNumber struct is stored as its string value (nullable for private customers).
        builder.Property(c => c.Cvr)
            .HasConversion(v => v == null ? null : v.Value.Value, s => s == null ? null : CvrNumber.Create(s))
            .HasMaxLength(8);
    }
}

internal sealed class DeliveryAddressConfiguration : IEntityTypeConfiguration<DeliveryAddress>
{
    public void Configure(EntityTypeBuilder<DeliveryAddress> builder)
    {
        builder.ToTable("delivery_addresses");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Street).HasMaxLength(200).IsRequired();
        builder.Property(a => a.PostalCode)
            .HasConversion(p => p.Value, s => PostalCode.Create(s))
            .HasMaxLength(4);
        builder.Property(a => a.City).HasMaxLength(100).IsRequired();
        builder.Property(a => a.AccessNotes).HasMaxLength(500);
        builder.HasOne<Customer>().WithMany().HasForeignKey(a => a.CustomerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(a => a.CustomerId);
    }
}

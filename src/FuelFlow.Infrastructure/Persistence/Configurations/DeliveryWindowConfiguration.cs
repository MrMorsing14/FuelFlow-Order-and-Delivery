using FuelFlow.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelFlow.Infrastructure.Persistence.Configurations;

internal sealed class DeliveryWindowConfiguration : IEntityTypeConfiguration<DeliveryWindow>
{
    public void Configure(EntityTypeBuilder<DeliveryWindow> builder)
    {
        builder.ToTable("delivery_windows");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).ValueGeneratedNever();
        builder.Property(w => w.Region).HasMaxLength(100).IsRequired();
        builder.HasIndex(w => new { w.Date, w.Region }).IsUnique();

        // Optimistic concurrency (like JPA's @Version) using Postgres' built-in xmin system column.
        // xmin changes on every UPDATE, so EF adds "WHERE xmin = @original" and throws
        // DbUpdateConcurrencyException if another transaction changed the row first.
        // It is a shadow property: the Domain entity knows nothing about it.
        builder.Property<uint>("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();
    }
}

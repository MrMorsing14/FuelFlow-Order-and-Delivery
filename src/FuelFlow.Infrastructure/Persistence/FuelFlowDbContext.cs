using FuelFlow.Domain.Customers;
using FuelFlow.Domain.Orders;
using FuelFlow.Domain.Products;
using FuelFlow.Domain.Scheduling;
using FuelFlow.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext (like JPA's EntityManager + persistence unit). Each DbSet is a table.
/// Mapping lives in separate IEntityTypeConfiguration classes (like having one mapping class per entity
/// instead of annotations on the entities, which keeps the Domain free of persistence attributes).
/// </summary>
public class FuelFlowDbContext(DbContextOptions<FuelFlowDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<DeliveryAddress> DeliveryAddresses => Set<DeliveryAddress>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<DeliveryWindow> DeliveryWindows => Set<DeliveryWindow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FuelFlowDbContext).Assembly);
}

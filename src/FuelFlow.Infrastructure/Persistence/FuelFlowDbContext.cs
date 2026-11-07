using FuelFlow.Application.Auth;
using FuelFlow.Domain.Customers;
using FuelFlow.Domain.Orders;
using FuelFlow.Domain.Products;
using FuelFlow.Domain.Scheduling;
using FuelFlow.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FuelFlow.Infrastructure.Persistence;

/// <summary>
/// EF Core DbContext (like JPA's EntityManager + persistence unit). Each DbSet is a table.
/// Mapping lives in separate IEntityTypeConfiguration classes (like having one mapping class per entity
/// instead of annotations on the entities, which keeps the Domain free of persistence attributes).
/// </summary>
public class FuelFlowDbContext(DbContextOptions<FuelFlowDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<DeliveryAddress> DeliveryAddresses => Set<DeliveryAddress>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<DeliveryWindow> DeliveryWindows => Set<DeliveryWindow>();

    // A pre-check in AuthService catches most duplicates; this catches two registrations racing each other.
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_users_Email" })
        {
            throw new EmailAlreadyRegisteredException();
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FuelFlowDbContext).Assembly);
}

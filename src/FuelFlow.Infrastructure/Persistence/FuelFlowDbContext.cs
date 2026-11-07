using FuelFlow.Application.Auth;
using FuelFlow.Application.Common;
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

    public void DiscardChanges() => ChangeTracker.Clear();

    // Turns database errors into application exceptions that the API maps to 409.
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The xmin check failed: someone else changed the same order or delivery window first.
            throw new ConcurrencyConflictException();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, "IX_users_Email"))
        {
            // A pre-check in AuthService catches most duplicates; this catches two registrations racing each other.
            throw new EmailAlreadyRegisteredException();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex, "IX_delivery_windows_Date_Region"))
        {
            throw new DeliveryWindowExistsException();
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex, string constraintName) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg && pg.ConstraintName == constraintName;

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FuelFlowDbContext).Assembly);
}

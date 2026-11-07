using FuelFlow.Domain.Customers;
using FuelFlow.Domain.Products;
using FuelFlow.Domain.Scheduling;
using FuelFlow.Domain.Users;
using FuelFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FuelFlow.Infrastructure.Seeding;

/// <summary>Seeds demo data for local development. Safe to call repeatedly: does nothing if users already exist.</summary>
public sealed class DevelopmentDataSeeder(
    FuelFlowDbContext db,
    IPasswordHasher<User> passwordHasher,
    TimeProvider timeProvider,
    IOptions<SeedOptions> options)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        // Products too: without seed credentials no users exist, and re-inserting products would hit their unique index.
        if (await db.Users.AnyAsync(cancellationToken) || await db.Products.AnyAsync(cancellationToken))
        {
            return;
        }

        var seed = options.Value;
        var now = timeProvider.GetUtcNow();

        AddStaff(seed.Admin, UserRole.Admin);
        AddStaff(seed.Dispatcher, UserRole.Dispatcher);

        if (seed.PrivateCustomer is { IsComplete: true } privateLogin)
        {
            var customer = Customer.CreatePrivate("Anna Hansen", "+4512345678", privateLogin.Email);
            db.Customers.Add(customer);
            db.DeliveryAddresses.Add(DeliveryAddress.Create(
                customer.Id, "Nørrebrogade 10", "2200", "København N", 3000, "Tank is behind the garage"));
            AddCustomerUser(privateLogin, customer);
        }

        if (seed.BusinessCustomer is { IsComplete: true } businessLogin)
        {
            var customer = Customer.CreateBusiness("Jensen Transport ApS", "10150817", "+4587654321", businessLogin.Email);
            db.Customers.Add(customer);
            db.DeliveryAddresses.Add(DeliveryAddress.Create(
                customer.Id, "Banegårdsgade 5", "8000", "Aarhus C", 20_000, "Gate code 1234, ask for the foreman"));
            AddCustomerUser(businessLogin, customer);
        }

        // Prices valid from yesterday so orders can be placed right away.
        db.Products.AddRange(
            Product.Create(ProductType.HeatingOil, 11.50m, now.AddDays(-1)),
            Product.Create(ProductType.Diesel, 12.75m, now.AddDays(-1)),
            Product.Create(ProductType.Hvo, 14.20m, now.AddDays(-1)));

        // One truck per region per day, for the next 14 days. Regions are defined by postal code ranges
        // in configuration (added together with the scheduler).
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        for (var day = 1; day <= 14; day++)
        {
            foreach (var region in new[] { "Copenhagen", "Aarhus" })
            {
                db.DeliveryWindows.Add(DeliveryWindow.Create(today.AddDays(day), region, 10_000));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private void AddStaff(SeedCredentials? login, UserRole role)
    {
        if (login is { IsComplete: true })
        {
            AddUser(login, role, customerId: null);
        }
    }

    private void AddCustomerUser(SeedCredentials login, Customer customer) =>
        AddUser(login, UserRole.Customer, customer.Id);

    private void AddUser(SeedCredentials login, UserRole role, Guid? customerId)
    {
        // The hash is computed with a placeholder user first: PasswordHasher<T> only uses the user object as a type marker.
        var hash = passwordHasher.HashPassword(null!, login.Password);
        db.Users.Add(User.Create(login.Email, hash, role, customerId));
    }
}

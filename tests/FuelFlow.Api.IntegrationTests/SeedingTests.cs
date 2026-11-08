using FuelFlow.Domain.Products;
using FuelFlow.Domain.Users;
using FuelFlow.Infrastructure.Seeding;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace FuelFlow.Api.IntegrationTests;

public class SeedingTests(FuelFlowApiFactory factory) : IntegrationTestBase(factory)
{
    private static readonly DateTimeOffset _now = new(2026, 3, 2, 8, 0, 0, TimeSpan.Zero);

    private static SeedOptions FullOptions() => new()
    {
        Admin = new SeedCredentials { Email = "admin@test.local", Password = "admin-pass" },
        Dispatcher = new SeedCredentials { Email = "disp@test.local", Password = "disp-pass" },
        PrivateCustomer = new SeedCredentials { Email = "private@test.local", Password = "private-pass" },
        BusinessCustomer = new SeedCredentials { Email = "business@test.local", Password = "business-pass" },
    };

    private static DevelopmentDataSeeder CreateSeeder(
        AsyncServiceScope scope, SeedOptions options, IPasswordHasher<User>? hasher = null) =>
        new(Db(scope), hasher ?? new PasswordHasher<User>(), new FakeTimeProvider(_now), Options.Create(options));

    [Fact]
    public async Task SeedAsync_EmptyDatabase_CreatesUsersCustomersProductsAndFourteenDaysOfWindows()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var scope = NewScope())
        {
            await CreateSeeder(scope, FullOptions()).SeedAsync(ct);
        }

        await using var verify = NewScope();
        var db = Db(verify);
        var users = await db.Users.ToListAsync(ct);
        users.Select(u => u.Role).ShouldBe(
            [UserRole.Customer, UserRole.Customer, UserRole.Dispatcher, UserRole.Admin], ignoreOrder: true);
        users.Where(u => u.Role == UserRole.Customer).ShouldAllBe(u => u.CustomerId != null);
        (await db.Customers.CountAsync(ct)).ShouldBe(2);
        (await db.DeliveryAddresses.CountAsync(ct)).ShouldBe(2);

        var products = await db.Products.Include(p => p.PriceHistory).ToListAsync(ct);
        products.Select(p => p.Type).ShouldBe([ProductType.HeatingOil, ProductType.Diesel, ProductType.Hvo, ProductType.Mgo], ignoreOrder: true);
        products.ShouldAllBe(p => p.PriceAt(_now) > 0);

        var windows = await db.DeliveryWindows.ToListAsync(ct);
        windows.Count.ShouldBe(28);
        windows.Min(w => w.Date).ShouldBe(new DateOnly(2026, 3, 3));
        windows.Max(w => w.Date).ShouldBe(new DateOnly(2026, 3, 16));
    }

    [Fact]
    public async Task SeedAsync_Password_IsStoredHashedAndVerifiable()
    {
        var ct = TestContext.Current.CancellationToken;
        var hasher = new PasswordHasher<User>();
        await using (var scope = NewScope())
        {
            await CreateSeeder(scope, FullOptions(), hasher).SeedAsync(ct);
        }

        await using var verify = NewScope();
        var admin = await Db(verify).Users.SingleAsync(u => u.Role == UserRole.Admin, ct);
        admin.PasswordHash.ShouldNotContain("admin-pass");
        hasher.VerifyHashedPassword(admin, admin.PasswordHash, "admin-pass").ShouldBe(PasswordVerificationResult.Success);
        hasher.VerifyHashedPassword(admin, admin.PasswordHash, "wrong").ShouldBe(PasswordVerificationResult.Failed);
    }

    [Fact]
    public async Task SeedAsync_RunTwice_DoesNotDuplicateData()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var scope = NewScope())
        {
            await CreateSeeder(scope, FullOptions()).SeedAsync(ct);
        }

        await using (var scope = NewScope())
        {
            await CreateSeeder(scope, FullOptions()).SeedAsync(ct);
        }

        await using var verify = NewScope();
        (await Db(verify).Users.CountAsync(ct)).ShouldBe(4);
        (await Db(verify).Products.CountAsync(ct)).ShouldBe(4);
    }

    [Fact]
    public async Task SeedAsync_NoCredentialsConfigured_SeedsNoUsersAndSurvivesSecondRun()
    {
        var ct = TestContext.Current.CancellationToken;
        for (var run = 0; run < 2; run++)
        {
            await using var scope = NewScope();
            await CreateSeeder(scope, new SeedOptions()).SeedAsync(ct);
        }

        await using var verify = NewScope();
        (await Db(verify).Users.CountAsync(ct)).ShouldBe(0);
    }
}

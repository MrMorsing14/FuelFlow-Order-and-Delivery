using FuelFlow.Application.Auth;
using FuelFlow.Domain.Customers;
using FuelFlow.Domain.Orders;
using FuelFlow.Domain.Pricing;
using FuelFlow.Domain.Products;
using FuelFlow.Domain.Scheduling;
using FuelFlow.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace FuelFlow.Api.IntegrationTests;

public class PersistenceTests(FuelFlowApiFactory factory) : IntegrationTestBase(factory)
{
    private static readonly DateTimeOffset _now = new(2026, 3, 2, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Database_AfterStartup_HasMigrationsAppliedAndIsEmpty()
    {
        await using var scope = NewScope();
        var db = Db(scope);

        (await db.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken)).ShouldNotBeEmpty();
        (await db.Orders.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task Order_SavedAndReloaded_KeepsPriceSnapshotAndFinalPrice()
    {
        var ct = TestContext.Current.CancellationToken;
        var customer = Customer.CreateBusiness("Jensen ApS", "10150817", "+4511111111", "j@example.com");
        var address = DeliveryAddress.Create(customer.Id, "Vejen 1", "8000", "Aarhus C", 20_000);
        var product = Product.Create(ProductType.Diesel, 12.5m, _now.AddDays(-1));
        var order = Order.Place(customer, address, product, 5000, new PriceCalculator(new PricingOptions()), _now);

        await using (var scope = NewScope())
        {
            var db = Db(scope);
            db.Customers.Add(customer);
            db.DeliveryAddresses.Add(address);
            db.Products.Add(product);
            db.Orders.Add(order);
            await db.SaveChangesAsync(ct);
        }

        await using (var scope = NewScope())
        {
            var loaded = await Db(scope).Orders.SingleAsync(ct);
            loaded.Status.ShouldBe(OrderStatus.Pending);
            loaded.Price.ShouldBe(order.Price);
            loaded.Price.DiscountAmount.ShouldBe(1250m);
            loaded.FinalPrice.ShouldBeNull();

            loaded.Confirm(_now);
            var windowId = await AddWindowAsync(Db(scope), ct);
            loaded.Schedule(windowId, _now);
            loaded.StartDelivery(_now);
            loaded.Deliver(4800, _now);
            await Db(scope).SaveChangesAsync(ct);
        }

        await using (var scope = NewScope())
        {
            var delivered = await Db(scope).Orders.SingleAsync(ct);
            delivered.Status.ShouldBe(OrderStatus.Delivered);
            delivered.DeliveredLitres.ShouldBe(4800);
            delivered.FinalPrice.ShouldNotBeNull();
            delivered.FinalPrice.NetAmount.ShouldBe(60_000m);
            delivered.Price.Litres.ShouldBe(5000, "the order-time snapshot is untouched");
        }
    }

    [Fact]
    public async Task Product_WithPriceChange_PersistsFullPriceHistory()
    {
        var ct = TestContext.Current.CancellationToken;
        var product = Product.Create(ProductType.HeatingOil, 10m, _now);
        product.ChangePrice(11m, _now.AddDays(1));

        await using (var scope = NewScope())
        {
            Db(scope).Products.Add(product);
            await Db(scope).SaveChangesAsync(ct);
        }

        await using (var scope = NewScope())
        {
            var loaded = await Db(scope).Products.SingleAsync(ct);
            loaded.PriceHistory.Count.ShouldBe(2);
            loaded.PriceAt(_now.AddHours(1)).ShouldBe(10m);
            loaded.PriceAt(_now.AddDays(2)).ShouldBe(11m);
        }
    }

    [Fact]
    public async Task DeliveryWindow_TwoContextsBookTheSameWindow_SecondSaveThrowsConcurrencyException()
    {
        var ct = TestContext.Current.CancellationToken;
        var window = DeliveryWindow.Create(new DateOnly(2026, 3, 10), "Copenhagen", 10_000);
        await using (var scope = NewScope())
        {
            Db(scope).DeliveryWindows.Add(window);
            await Db(scope).SaveChangesAsync(ct);
        }

        await using var scopeA = NewScope();
        await using var scopeB = NewScope();
        var windowA = await Db(scopeA).DeliveryWindows.SingleAsync(ct);
        var windowB = await Db(scopeB).DeliveryWindows.SingleAsync(ct);
        windowA.Book(6000);
        windowB.Book(6000);

        await Db(scopeA).SaveChangesAsync(ct);
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => Db(scopeB).SaveChangesAsync(ct));

        await using var verify = NewScope();
        (await Db(verify).DeliveryWindows.SingleAsync(ct)).BookedLitres.ShouldBe(6000);
    }

    [Fact]
    public async Task SaveChanges_TwoUsersWithSameEmail_ThrowsEmailAlreadyRegistered()
    {
        // Simulates two registrations racing: the AuthService pre-check passed for both, the unique index stops the second.
        await using var scope = NewScope();
        Db(scope).Users.AddRange(
            User.Create("dup@example.com", "hash", UserRole.Admin),
            User.Create("dup@example.com", "hash", UserRole.Dispatcher));

        await Should.ThrowAsync<EmailAlreadyRegisteredException>(() => Db(scope).SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Customer_WithAndWithoutCvr_RoundTripsValueConverters()
    {
        var ct = TestContext.Current.CancellationToken;
        var business = Customer.CreateBusiness("Jensen ApS", "10150817", "+4511111111", "j@example.com");
        var priv = Customer.CreatePrivate("Anna Hansen", "+4522222222", "a@example.com");
        await using (var scope = NewScope())
        {
            Db(scope).Customers.AddRange(business, priv);
            Db(scope).DeliveryAddresses.Add(DeliveryAddress.Create(priv.Id, "Vejen 1", "2100", "København Ø", 3000, "Behind garage"));
            await Db(scope).SaveChangesAsync(ct);
        }

        await using (var scope = NewScope())
        {
            var customers = await Db(scope).Customers.ToListAsync(ct);
            customers.Single(c => c.Id == business.Id).Cvr!.Value.Value.ShouldBe("10150817");
            customers.Single(c => c.Id == priv.Id).Cvr.ShouldBeNull();
            (await Db(scope).DeliveryAddresses.SingleAsync(ct)).PostalCode.Value.ShouldBe("2100");
        }
    }

    private static async Task<Guid> AddWindowAsync(FuelFlow.Infrastructure.Persistence.FuelFlowDbContext db, CancellationToken ct)
    {
        var window = DeliveryWindow.Create(new DateOnly(2026, 3, 5), "Aarhus", 10_000);
        db.DeliveryWindows.Add(window);
        await db.SaveChangesAsync(ct);
        return window.Id;
    }
}

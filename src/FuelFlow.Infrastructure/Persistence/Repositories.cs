using FuelFlow.Application.Common;
using FuelFlow.Application.Customers;
using FuelFlow.Application.Dispatch;
using FuelFlow.Application.Orders;
using FuelFlow.Application.Products;
using FuelFlow.Domain.Common;
using FuelFlow.Domain.Customers;
using FuelFlow.Domain.Orders;
using FuelFlow.Domain.Products;
using FuelFlow.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Infrastructure.Persistence;

internal sealed class AddressRepository(FuelFlowDbContext db) : IAddressRepository
{
    public async Task<IReadOnlyList<DeliveryAddress>> ListAsync(Guid customerId, CancellationToken cancellationToken) =>
        await db.DeliveryAddresses.AsNoTracking().Where(a => a.CustomerId == customerId).OrderBy(a => a.Id).ToListAsync(cancellationToken);

    public Task<DeliveryAddress?> GetAsync(Guid addressId, Guid customerId, CancellationToken cancellationToken) =>
        db.DeliveryAddresses.FirstOrDefaultAsync(a => a.Id == addressId && a.CustomerId == customerId, cancellationToken);

    public Task<bool> HasOrdersAsync(Guid addressId, CancellationToken cancellationToken) =>
        db.Orders.AnyAsync(o => o.DeliveryAddressId == addressId, cancellationToken);

    public void Add(DeliveryAddress address) => db.DeliveryAddresses.Add(address);

    public void Remove(DeliveryAddress address) => db.DeliveryAddresses.Remove(address);
}

internal sealed class ProductRepository(FuelFlowDbContext db) : IProductRepository
{
    public async Task<IReadOnlyList<Product>> ListAsync(CancellationToken cancellationToken) =>
        await db.Products.AsNoTracking().ToListAsync(cancellationToken);

    public Task<Product?> GetAsync(Guid productId, CancellationToken cancellationToken) =>
        db.Products.FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);
}

internal sealed class OrderRepository(FuelFlowDbContext db) : IOrderRepository
{
    public void Add(Order order) => db.Orders.Add(order);

    public Task<Order?> GetAsync(Guid orderId, Guid customerId, CancellationToken cancellationToken) =>
        db.Orders.FirstOrDefaultAsync(o => o.Id == orderId && o.CustomerId == customerId, cancellationToken);

    public async Task<PagedResult<Order>> PageAsync(Guid customerId, PageRequest page, CancellationToken cancellationToken)
    {
        var query = db.Orders.AsNoTracking().Where(o => o.CustomerId == customerId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Id)
            .Skip((page.Page - 1) * page.PageSize).Take(page.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Order>(items, page.Page, page.PageSize, total);
    }

    public Task<Order?> GetAnyAsync(Guid orderId, CancellationToken cancellationToken) =>
        db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

    public async Task<PagedResult<DispatchOrderRow>> PageForDispatchAsync(
        DispatchOrderQuery filter, PageRequest page, CancellationToken cancellationToken)
    {
        // Joins by id (the entities have no navigation properties). The left join to windows keeps unscheduled orders.
        var query =
            from o in db.Orders.AsNoTracking()
            join a in db.DeliveryAddresses on o.DeliveryAddressId equals a.Id
            join c in db.Customers on o.CustomerId equals c.Id
            join w in db.DeliveryWindows on o.DeliveryWindowId equals w.Id into windows
            from w in windows.DefaultIfEmpty()
            select new { Order = o, Customer = c, Address = a, Date = (DateOnly?)w.Date };

        if (filter.Status is { } status)
        {
            query = query.Where(x => x.Order.Status == status);
        }

        if (filter.Date is { } date)
        {
            query = query.Where(x => x.Date == date);
        }

        if (filter.Region is { } region)
        {
            // PostalCode is stored through a value converter, which EF cannot compare with < or >.
            // A region is at most a few thousand codes, so "code IN (list)" is simple and fast enough.
            var codes = Enumerable.Range(region.From, region.To - region.From + 1)
                .Select(n => PostalCode.Create(n.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                .ToList();
            query = query.Where(x => codes.Contains(x.Address.PostalCode));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(x => x.Order.CreatedAt).ThenBy(x => x.Order.Id)
            .Skip((page.Page - 1) * page.PageSize).Take(page.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<DispatchOrderRow>(
            [.. items.Select(x => new DispatchOrderRow(x.Order, x.Customer, x.Address, x.Date))], page.Page, page.PageSize, total);
    }

    public async Task<IReadOnlyList<ConfirmedOrder>> ListConfirmedAsync(CancellationToken cancellationToken)
    {
        // shortcut: loads every confirmed order that has no window yet. Fine for hundreds; batch it if the backlog ever grows to many thousands.
        var rows = await (
            from o in db.Orders.AsNoTracking()
            join a in db.DeliveryAddresses on o.DeliveryAddressId equals a.Id
            where o.Status == OrderStatus.Confirmed && o.DeliveryWindowId == null
            select new { o.Id, o.OrderedLitres, a.PostalCode, o.CreatedAt })
            .ToListAsync(cancellationToken);

        return [.. rows.Select(r => new ConfirmedOrder(r.Id, r.OrderedLitres, r.PostalCode.Value, r.CreatedAt))];
    }
}

internal sealed class DeliveryWindowRepository(FuelFlowDbContext db) : IDeliveryWindowRepository
{
    public Task<DeliveryWindow?> GetAsync(Guid windowId, CancellationToken cancellationToken) =>
        db.DeliveryWindows.FirstOrDefaultAsync(w => w.Id == windowId, cancellationToken);

    public async Task<IReadOnlyList<DeliveryWindow>> ListAsync(DateOnly from, DateOnly? to, string? region, CancellationToken cancellationToken)
    {
        var query = db.DeliveryWindows.AsNoTracking().Where(w => w.Date >= from);
        if (to is { } last)
        {
            query = query.Where(w => w.Date <= last);
        }

        if (region is not null)
        {
            query = query.Where(w => w.Region == region);
        }

        return await query.OrderBy(w => w.Date).ThenBy(w => w.Region).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DeliveryWindow>> ListOpenAsync(DateOnly fromDate, CancellationToken cancellationToken) =>
        await db.DeliveryWindows.AsNoTracking()
            .Where(w => w.Date >= fromDate && w.CapacityLitres > w.BookedLitres)
            .OrderBy(w => w.Date).ThenBy(w => w.Id)
            .ToListAsync(cancellationToken);

    public void Add(DeliveryWindow window) => db.DeliveryWindows.Add(window);
}

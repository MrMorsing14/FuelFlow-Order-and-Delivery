using FuelFlow.Application.Common;
using FuelFlow.Application.Customers;
using FuelFlow.Application.Orders;
using FuelFlow.Application.Products;
using FuelFlow.Domain.Customers;
using FuelFlow.Domain.Orders;
using FuelFlow.Domain.Products;
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
}

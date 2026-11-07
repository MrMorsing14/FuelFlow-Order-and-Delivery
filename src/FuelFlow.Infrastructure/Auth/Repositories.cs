using FuelFlow.Application.Auth;
using FuelFlow.Domain.Customers;
using FuelFlow.Domain.Users;
using FuelFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FuelFlow.Infrastructure.Auth;

internal sealed class UserRepository(FuelFlowDbContext db) : IUserRepository
{
    public Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken) =>
        db.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

    public void Add(User user) => db.Users.Add(user);
}

internal sealed class CustomerRepository(FuelFlowDbContext db) : ICustomerRepository
{
    public void Add(Customer customer) => db.Customers.Add(customer);
}

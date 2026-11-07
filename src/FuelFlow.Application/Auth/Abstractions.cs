using FuelFlow.Domain.Customers;
using FuelFlow.Domain.Users;

namespace FuelFlow.Application.Auth;

// Interfaces the Application layer needs; Infrastructure implements them (dependency inversion).

public interface IUserRepository
{
    Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken);

    void Add(User user);
}

public interface ICustomerRepository
{
    Task<Customer?> GetAsync(Guid customerId, CancellationToken cancellationToken);

    void Add(Customer customer);
}

/// <summary>Commits everything added/changed through the repositories in one database transaction.</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IPasswordService
{
    string Hash(string password);

    bool Verify(string passwordHash, string password);
}

public interface ITokenService
{
    AccessToken Create(User user);
}

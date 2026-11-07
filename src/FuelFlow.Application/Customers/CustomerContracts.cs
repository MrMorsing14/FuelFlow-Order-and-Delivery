using FuelFlow.Application.Auth;
using FuelFlow.Domain.Customers;

namespace FuelFlow.Application.Customers;

public sealed record CustomerDto(Guid Id, CustomerType Type, string Email, string Phone, string? FullName, string? CompanyName, string? Cvr);

public sealed record UpdateCustomerRequest(string Phone, string? FullName, string? CompanyName, string? Cvr);

public sealed record AddressDto(Guid Id, string Street, string PostalCode, string City, int TankCapacityLitres, string? AccessNotes);

/// <summary>Used for both creating and replacing an address (PUT replaces all fields).</summary>
public sealed record AddressRequest(string Street, string PostalCode, string City, int TankCapacityLitres, string? AccessNotes);

public interface IAddressRepository
{
    Task<IReadOnlyList<DeliveryAddress>> ListAsync(Guid customerId, CancellationToken cancellationToken);

    /// <summary>Returns null if the address does not exist OR belongs to another customer.</summary>
    Task<DeliveryAddress?> GetAsync(Guid addressId, Guid customerId, CancellationToken cancellationToken);

    Task<bool> HasOrdersAsync(Guid addressId, CancellationToken cancellationToken);

    void Add(DeliveryAddress address);

    void Remove(DeliveryAddress address);
}

public static class CustomerMappings
{
    public static CustomerDto ToDto(this Customer c) =>
        new(c.Id, c.Type, c.Email, c.Phone, c.FullName, c.CompanyName, c.Cvr?.Value);

    public static AddressDto ToDto(this DeliveryAddress a) =>
        new(a.Id, a.Street, a.PostalCode.Value, a.City, a.TankCapacityLitres, a.AccessNotes);
}

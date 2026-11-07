using FluentValidation;
using FuelFlow.Application.Auth;
using FuelFlow.Application.Common;
using FuelFlow.Domain.Customers;

namespace FuelFlow.Application.Customers;

/// <summary>
/// Self-service for the logged-in customer. Every method takes the customer id from the JWT, never from the request,
/// and looks things up by (id, customerId) so another customer's data simply "does not exist".
/// </summary>
public sealed class CustomerService(
    ICustomerRepository customers,
    IAddressRepository addresses,
    IUnitOfWork unitOfWork,
    IValidator<UpdateCustomerRequest> updateValidator,
    IValidator<AddressRequest> addressValidator)
{
    public async Task<CustomerDto> GetAsync(Guid customerId, CancellationToken ct) =>
        (await FindCustomerAsync(customerId, ct)).ToDto();

    public async Task<CustomerDto> UpdateAsync(Guid customerId, UpdateCustomerRequest request, CancellationToken ct)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);
        var customer = await FindCustomerAsync(customerId, ct);

        customer.Update(request.Phone, request.FullName, request.CompanyName, request.Cvr);
        await unitOfWork.SaveChangesAsync(ct);
        return customer.ToDto();
    }

    public async Task<IReadOnlyList<AddressDto>> ListAddressesAsync(Guid customerId, CancellationToken ct) =>
        [.. (await addresses.ListAsync(customerId, ct)).Select(a => a.ToDto())];

    public async Task<AddressDto> GetAddressAsync(Guid customerId, Guid addressId, CancellationToken ct) =>
        (await FindAddressAsync(customerId, addressId, ct)).ToDto();

    public async Task<AddressDto> CreateAddressAsync(Guid customerId, AddressRequest request, CancellationToken ct)
    {
        await addressValidator.ValidateAndThrowAsync(request, ct);
        var address = DeliveryAddress.Create(
            customerId, request.Street, request.PostalCode, request.City, request.TankCapacityLitres, request.AccessNotes);

        addresses.Add(address);
        await unitOfWork.SaveChangesAsync(ct);
        return address.ToDto();
    }

    public async Task<AddressDto> UpdateAddressAsync(Guid customerId, Guid addressId, AddressRequest request, CancellationToken ct)
    {
        await addressValidator.ValidateAndThrowAsync(request, ct);
        var address = await FindAddressAsync(customerId, addressId, ct);

        // Orders point at the address by id, so moving it would silently change where old and planned orders go.
        var movesLocation = request.Street.Trim() != address.Street
            || request.PostalCode.Trim() != address.PostalCode.Value
            || request.City.Trim() != address.City;
        if (movesLocation && await addresses.HasOrdersAsync(addressId, ct))
        {
            throw new AddressInUseException("The address has orders, so street, postal code and city cannot change. Create a new address instead.");
        }

        address.Update(request.Street, request.PostalCode, request.City, request.TankCapacityLitres, request.AccessNotes);
        await unitOfWork.SaveChangesAsync(ct);
        return address.ToDto();
    }

    public async Task DeleteAddressAsync(Guid customerId, Guid addressId, CancellationToken ct)
    {
        var address = await FindAddressAsync(customerId, addressId, ct);
        if (await addresses.HasOrdersAsync(addressId, ct))
        {
            throw new AddressInUseException("The address has orders and cannot be deleted.");
        }

        addresses.Remove(address);
        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task<Customer> FindCustomerAsync(Guid customerId, CancellationToken ct) =>
        await customers.GetAsync(customerId, ct) ?? throw new NotFoundException("Customer");

    private async Task<DeliveryAddress> FindAddressAsync(Guid customerId, Guid addressId, CancellationToken ct) =>
        await addresses.GetAsync(addressId, customerId, ct) ?? throw new NotFoundException("Address");
}

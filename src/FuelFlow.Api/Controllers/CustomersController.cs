using FuelFlow.Application.Customers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FuelFlow.Api.Controllers;

[ApiController]
[Authorize(Roles = "Customer")]
[Route("api/customers/me")]
public sealed class CustomersController(CustomerService customers) : ControllerBase
{
    [HttpGet]
    public Task<CustomerDto> Get(CancellationToken ct) => customers.GetAsync(User.CustomerId(), ct);

    [HttpPut]
    public Task<CustomerDto> Update(UpdateCustomerRequest request, CancellationToken ct) =>
        customers.UpdateAsync(User.CustomerId(), request, ct);
}

[ApiController]
[Authorize(Roles = "Customer")]
[Route("api/customers/me/addresses")]
public sealed class AddressesController(CustomerService customers) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<AddressDto>> List(CancellationToken ct) => customers.ListAddressesAsync(User.CustomerId(), ct);

    [HttpGet("{id:guid}")]
    public Task<AddressDto> Get(Guid id, CancellationToken ct) => customers.GetAddressAsync(User.CustomerId(), id, ct);

    [HttpPost]
    [ProducesResponseType<AddressDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(AddressRequest request, CancellationToken ct)
    {
        var address = await customers.CreateAddressAsync(User.CustomerId(), request, ct);
        return CreatedAtAction(nameof(Get), new { id = address.Id }, address);
    }

    [HttpPut("{id:guid}")]
    public Task<AddressDto> Update(Guid id, AddressRequest request, CancellationToken ct) =>
        customers.UpdateAddressAsync(User.CustomerId(), id, request, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await customers.DeleteAddressAsync(User.CustomerId(), id, ct);
        return NoContent();
    }
}

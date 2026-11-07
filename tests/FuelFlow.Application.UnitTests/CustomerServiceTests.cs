using FuelFlow.Application.Auth;
using FuelFlow.Application.Common;
using FuelFlow.Application.Customers;
using FuelFlow.Domain.Customers;
using NSubstitute;
using Shouldly;

namespace FuelFlow.Application.UnitTests;

public class CustomerServiceTests
{
    private readonly ICustomerRepository _customers = Substitute.For<ICustomerRepository>();
    private readonly IAddressRepository _addresses = Substitute.For<IAddressRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly CustomerService _service;

    public CustomerServiceTests() =>
        _service = new CustomerService(_customers, _addresses, _unitOfWork, new UpdateCustomerRequestValidator(), new AddressRequestValidator());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AddressRequest ValidAddress() => new("Vejen 1", "2100", "København Ø", 3000, null);

    [Fact]
    public async Task CreateAddressAsync_Valid_AddsAddressForTheCallerAndSaves()
    {
        var customerId = Guid.NewGuid();

        var dto = await _service.CreateAddressAsync(customerId, ValidAddress(), Ct);

        _addresses.Received(1).Add(Arg.Is<DeliveryAddress>(a => a.CustomerId == customerId && a.Id == dto.Id));
        await _unitOfWork.Received(1).SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task DeleteAddressAsync_AddressWithOrders_ThrowsAndRemovesNothing()
    {
        var address = DeliveryAddress.Create(Guid.NewGuid(), "Vejen 1", "2100", "København Ø", 3000);
        _addresses.GetAsync(address.Id, address.CustomerId, Arg.Any<CancellationToken>()).Returns(address);
        _addresses.HasOrdersAsync(address.Id, Arg.Any<CancellationToken>()).Returns(true);

        await Should.ThrowAsync<AddressInUseException>(() => _service.DeleteAddressAsync(address.CustomerId, address.Id, Ct));

        _addresses.DidNotReceive().Remove(Arg.Any<DeliveryAddress>());
    }

    [Fact]
    public async Task UpdateAddressAsync_AddressWithOrdersMovesLocation_ThrowsAndSavesNothing()
    {
        var address = DeliveryAddress.Create(Guid.NewGuid(), "Vejen 1", "2100", "København Ø", 3000);
        _addresses.GetAsync(address.Id, address.CustomerId, Arg.Any<CancellationToken>()).Returns(address);
        _addresses.HasOrdersAsync(address.Id, Arg.Any<CancellationToken>()).Returns(true);

        await Should.ThrowAsync<AddressInUseException>(() => _service.UpdateAddressAsync(
            address.CustomerId, address.Id, new AddressRequest("Vejen 1", "8000", "Aarhus C", 3000, null), Ct));

        address.PostalCode.Value.ShouldBe("2100");
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateAddressAsync_AddressWithOrdersOnlyNotesAndTankChange_Succeeds()
    {
        var address = DeliveryAddress.Create(Guid.NewGuid(), "Vejen 1", "2100", "København Ø", 3000);
        _addresses.GetAsync(address.Id, address.CustomerId, Arg.Any<CancellationToken>()).Returns(address);
        _addresses.HasOrdersAsync(address.Id, Arg.Any<CancellationToken>()).Returns(true);

        var dto = await _service.UpdateAddressAsync(
            address.CustomerId, address.Id, new AddressRequest(" Vejen 1 ", "2100", "København Ø", 5000, "Gate code 1234"), Ct);

        (dto.TankCapacityLitres, dto.AccessNotes).ShouldBe((5000, "Gate code 1234"));
    }

    [Fact]
    public async Task DeleteAddressAsync_AddressWithoutOrders_RemovesAndSaves()
    {
        var address = DeliveryAddress.Create(Guid.NewGuid(), "Vejen 1", "2100", "København Ø", 3000);
        _addresses.GetAsync(address.Id, address.CustomerId, Arg.Any<CancellationToken>()).Returns(address);

        await _service.DeleteAddressAsync(address.CustomerId, address.Id, Ct);

        _addresses.Received(1).Remove(address);
        await _unitOfWork.Received(1).SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task UpdateAddressAsync_AddressOfAnotherCustomer_ThrowsNotFound()
    {
        await Should.ThrowAsync<NotFoundException>(() => _service.UpdateAddressAsync(Guid.NewGuid(), Guid.NewGuid(), ValidAddress(), Ct));
    }

    [Theory]
    [InlineData("", "2100", "København", 3000, "Street")]
    [InlineData("Vejen 1", "0999", "København", 3000, "PostalCode")]
    [InlineData("Vejen 1", "2100", "", 3000, "City")]
    [InlineData("Vejen 1", "2100", "København", 0, "TankCapacityLitres")]
    public void AddressValidator_InvalidField_FailsOnThatField(string street, string postalCode, string city, int tank, string field)
    {
        var result = new AddressRequestValidator().Validate(new AddressRequest(street, postalCode, city, tank, null));

        result.Errors.ShouldContain(e => e.PropertyName == field);
    }
}

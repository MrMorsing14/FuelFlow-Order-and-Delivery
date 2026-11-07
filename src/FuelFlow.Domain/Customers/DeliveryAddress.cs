using FuelFlow.Domain.Common;

namespace FuelFlow.Domain.Customers;

public sealed class DeliveryAddress
{
    private DeliveryAddress()
    {
    }

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public string Street { get; private set; } = "";
    public PostalCode PostalCode { get; private set; }
    public string City { get; private set; } = "";
    public int TankCapacityLitres { get; private set; }
    public string? AccessNotes { get; private set; }

    public static DeliveryAddress Create(
        Guid customerId, string? street, string? postalCode, string? city, int tankCapacityLitres, string? accessNotes = null)
    {
        var address = new DeliveryAddress { Id = Guid.CreateVersion7(), CustomerId = customerId };
        address.Update(street, postalCode, city, tankCapacityLitres, accessNotes);
        return address;
    }

    public void Update(string? street, string? postalCode, string? city, int tankCapacityLitres, string? accessNotes)
    {
        // Validate everything first so a failed update leaves the address unchanged.
        var newStreet = Guard.NotBlank(street, "Street");
        var newPostalCode = PostalCode.Create(postalCode);
        var newCity = Guard.NotBlank(city, "City");
        var newTank = Guard.Positive(tankCapacityLitres, "Tank capacity");

        Street = newStreet;
        PostalCode = newPostalCode;
        City = newCity;
        TankCapacityLitres = newTank;
        AccessNotes = string.IsNullOrWhiteSpace(accessNotes) ? null : accessNotes.Trim();
    }
}

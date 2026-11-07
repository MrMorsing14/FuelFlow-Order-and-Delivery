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
        Guid customerId, string? street, string? postalCode, string? city, int tankCapacityLitres, string? accessNotes = null) => new()
        {
            Id = Guid.CreateVersion7(),
            CustomerId = customerId,
            Street = Guard.NotBlank(street, "Street"),
            PostalCode = PostalCode.Create(postalCode),
            City = Guard.NotBlank(city, "City"),
            TankCapacityLitres = Guard.Positive(tankCapacityLitres, "Tank capacity"),
            AccessNotes = string.IsNullOrWhiteSpace(accessNotes) ? null : accessNotes.Trim(),
        };
}

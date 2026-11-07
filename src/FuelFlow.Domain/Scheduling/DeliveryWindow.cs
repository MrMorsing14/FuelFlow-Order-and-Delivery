using FuelFlow.Domain.Common;

namespace FuelFlow.Domain.Scheduling;

/// <summary>One truck, one region, one day. Booked litres can never exceed capacity.</summary>
public sealed class DeliveryWindow
{
    private DeliveryWindow()
    {
    }

    public Guid Id { get; private set; }
    public DateOnly Date { get; private set; }
    public string Region { get; private set; } = "";
    public int CapacityLitres { get; private set; }
    public int BookedLitres { get; private set; }

    public int RemainingLitres => CapacityLitres - BookedLitres;

    public static DeliveryWindow Create(DateOnly date, string? region, int capacityLitres) => new()
    {
        Id = Guid.CreateVersion7(),
        Date = date,
        Region = Guard.NotBlank(region, "Region"),
        CapacityLitres = Guard.Positive(capacityLitres, "Capacity"),
    };

    public void Book(int litres)
    {
        Guard.Positive(litres, "Litres");
        if (litres > RemainingLitres)
        {
            throw new DeliveryWindowFullException(Id, litres, RemainingLitres);
        }

        BookedLitres += litres;
    }

    public void Release(int litres)
    {
        Guard.Positive(litres, "Litres");
        if (litres > BookedLitres)
        {
            throw new DomainValidationException("Cannot release more litres than are booked.");
        }

        BookedLitres -= litres;
    }
}

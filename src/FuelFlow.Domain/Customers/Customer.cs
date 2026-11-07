using FuelFlow.Domain.Common;

namespace FuelFlow.Domain.Customers;

public sealed class Customer
{
    private Customer()
    {
        // Parameterless constructor for EF Core (like JPA requiring a default constructor).
    }

    public Guid Id { get; private set; }
    public CustomerType Type { get; private set; }
    public string Phone { get; private set; } = "";
    public string Email { get; private set; } = "";

    /// <summary>Set for Private customers.</summary>
    public string? FullName { get; private set; }

    /// <summary>Set for Business customers.</summary>
    public string? CompanyName { get; private set; }

    public CvrNumber? Cvr { get; private set; }

    public static Customer CreatePrivate(string? fullName, string? phone, string? email) => new()
    {
        Id = Guid.CreateVersion7(),
        Type = CustomerType.Private,
        FullName = Guard.NotBlank(fullName, "Full name"),
        Phone = Guard.NotBlank(phone, "Phone"),
        Email = Guard.NotBlank(email, "Email"),
    };

    public static Customer CreateBusiness(string? companyName, string? cvr, string? phone, string? email) => new()
    {
        Id = Guid.CreateVersion7(),
        Type = CustomerType.Business,
        CompanyName = Guard.NotBlank(companyName, "Company name"),
        Cvr = CvrNumber.Create(cvr),
        Phone = Guard.NotBlank(phone, "Phone"),
        Email = Guard.NotBlank(email, "Email"),
    };
}

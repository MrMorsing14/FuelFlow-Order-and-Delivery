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

    /// <summary>
    /// Updates contact details. A private customer's name or a business customer's company and CVR are changed,
    /// depending on the customer's own type; the type and the email (the login) cannot change here.
    /// </summary>
    public void Update(string? phone, string? fullName, string? companyName, string? cvr)
    {
        // Validate everything first so a failed update leaves the customer unchanged.
        var newPhone = Guard.NotBlank(phone, "Phone");
        if (Type == CustomerType.Private)
        {
            var newName = Guard.NotBlank(fullName, "Full name");
            FullName = newName;
        }
        else
        {
            var newCompany = Guard.NotBlank(companyName, "Company name");
            var newCvr = CvrNumber.Create(cvr);
            CompanyName = newCompany;
            Cvr = newCvr;
        }

        Phone = newPhone;
    }

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

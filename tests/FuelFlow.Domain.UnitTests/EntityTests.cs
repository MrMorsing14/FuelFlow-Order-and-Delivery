using FuelFlow.Domain.Common;
using FuelFlow.Domain.Customers;
using FuelFlow.Domain.Products;
using FuelFlow.Domain.Scheduling;
using FuelFlow.Domain.Users;
using Shouldly;

namespace FuelFlow.Domain.UnitTests;

public class CustomerTests
{
    [Fact]
    public void CreateBusiness_ValidInput_StoresCompanyAndCvr()
    {
        var customer = Customer.CreateBusiness("Hansen ApS", TestData.ValidCvr, "+4511111111", "a@b.dk");

        customer.Type.ShouldBe(CustomerType.Business);
        customer.CompanyName.ShouldBe("Hansen ApS");
        customer.Cvr.ShouldBe(CvrNumber.Create(TestData.ValidCvr));
        customer.FullName.ShouldBeNull();
    }

    [Theory]
    [InlineData("Hansen ApS", "12345678")]
    [InlineData("Hansen ApS", null)]
    [InlineData("", TestData.ValidCvr)]
    [InlineData(null, TestData.ValidCvr)]
    public void CreateBusiness_InvalidCvrOrMissingCompany_Throws(string? company, string? cvr)
    {
        Should.Throw<DomainValidationException>(() => Customer.CreateBusiness(company, cvr, "+4511111111", "a@b.dk"));
    }

    [Fact]
    public void CreatePrivate_ValidInput_StoresFullNameWithoutCvr()
    {
        var customer = Customer.CreatePrivate("Anna Hansen", "+4511111111", "a@b.dk");

        customer.Type.ShouldBe(CustomerType.Private);
        customer.FullName.ShouldBe("Anna Hansen");
        customer.Cvr.ShouldBeNull();
    }

    [Theory]
    [InlineData(null, "+4511111111", "a@b.dk")]
    [InlineData("Anna", "", "a@b.dk")]
    [InlineData("Anna", "+4511111111", " ")]
    public void CreatePrivate_MissingRequiredField_Throws(string? name, string? phone, string? email)
    {
        Should.Throw<DomainValidationException>(() => Customer.CreatePrivate(name, phone, email));
    }
}

public class DeliveryAddressTests
{
    [Fact]
    public void Create_ValidInput_TrimsAndStores()
    {
        var address = DeliveryAddress.Create(Guid.NewGuid(), " Vejen 1 ", "2100", "København Ø", 3000, "  Tank behind garage ");

        address.Street.ShouldBe("Vejen 1");
        address.PostalCode.Value.ShouldBe("2100");
        address.AccessNotes.ShouldBe("Tank behind garage");
    }

    [Fact]
    public void Create_BlankAccessNotes_StoresNull()
    {
        DeliveryAddress.Create(Guid.NewGuid(), "Vejen 1", "2100", "København Ø", 3000, "  ").AccessNotes.ShouldBeNull();
    }

    [Theory]
    [InlineData("", "2100", "By", 3000)]
    [InlineData("Vejen 1", "123", "By", 3000)]
    [InlineData("Vejen 1", "2100", "", 3000)]
    [InlineData("Vejen 1", "2100", "By", 0)]
    [InlineData("Vejen 1", "2100", "By", -1)]
    public void Create_InvalidField_Throws(string street, string postalCode, string city, int tank)
    {
        Should.Throw<DomainValidationException>(
            () => DeliveryAddress.Create(Guid.NewGuid(), street, postalCode, city, tank));
    }
}

public class UserTests
{
    [Fact]
    public void Create_CustomerRoleWithCustomerId_NormalisesEmail()
    {
        var user = User.Create("  Anna@Example.COM ", "hash", UserRole.Customer, Guid.NewGuid());

        user.Email.ShouldBe("anna@example.com");
    }

    [Fact]
    public void Create_CustomerRoleWithoutCustomerId_Throws()
    {
        Should.Throw<DomainValidationException>(() => User.Create("a@b.dk", "hash", UserRole.Customer));
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Dispatcher)]
    public void Create_StaffRoleWithCustomerId_Throws(UserRole role)
    {
        Should.Throw<DomainValidationException>(() => User.Create("a@b.dk", "hash", role, Guid.NewGuid()));
    }

    [Theory]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Dispatcher)]
    public void Create_StaffRoleWithoutCustomerId_Succeeds(UserRole role)
    {
        User.Create("a@b.dk", "hash", role).CustomerId.ShouldBeNull();
    }
}

public class ProductPriceHistoryTests
{
    [Fact]
    public void ChangePrice_NewerValidFrom_KeepsOldPriceInHistory()
    {
        var product = Product.Create(ProductType.Diesel, 10m, TestData.Now);

        product.ChangePrice(12m, TestData.Now.AddDays(1));

        product.PriceHistory.Count.ShouldBe(2);
        product.PriceAt(TestData.Now.AddHours(23)).ShouldBe(10m);
        product.PriceAt(TestData.Now.AddDays(1)).ShouldBe(12m);
        product.PriceAt(TestData.Now.AddDays(30)).ShouldBe(12m);
    }

    [Fact]
    public void ChangePrice_NotAfterLatestValidFrom_Throws()
    {
        var product = Product.Create(ProductType.Diesel, 10m, TestData.Now);

        Should.Throw<DomainValidationException>(() => product.ChangePrice(12m, TestData.Now));
        Should.Throw<DomainValidationException>(() => product.ChangePrice(12m, TestData.Now.AddDays(-1)));
        product.PriceHistory.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ChangePrice_NonPositivePrice_Throws(int price)
    {
        var product = Product.Create(ProductType.Hvo, 10m, TestData.Now);

        Should.Throw<DomainValidationException>(() => product.ChangePrice(price, TestData.Now.AddDays(1)));
    }

    [Fact]
    public void PriceAt_BeforeFirstPrice_Throws()
    {
        var product = Product.Create(ProductType.HeatingOil, 10m, TestData.Now);

        Should.Throw<DomainValidationException>(() => product.PriceAt(TestData.Now.AddSeconds(-1)));
    }
}

public class DeliveryWindowTests
{
    private static DeliveryWindow Window(int capacity = 10_000) =>
        DeliveryWindow.Create(new DateOnly(2026, 1, 20), "Copenhagen", capacity);

    [Fact]
    public void Book_WithinCapacity_IncreasesBookedLitres()
    {
        var window = Window();

        window.Book(4000);
        window.Book(6000);

        window.BookedLitres.ShouldBe(10_000);
        window.RemainingLitres.ShouldBe(0);
    }

    [Fact]
    public void Book_ExceedingCapacity_ThrowsAndLeavesBookingUnchanged()
    {
        var window = Window();
        window.Book(9000);

        Should.Throw<DeliveryWindowFullException>(() => window.Book(1001));

        window.BookedLitres.ShouldBe(9000);
    }

    [Fact]
    public void Release_BookedLitres_FreesCapacity()
    {
        var window = Window();
        window.Book(5000);

        window.Release(2000);

        window.RemainingLitres.ShouldBe(7000);
    }

    [Fact]
    public void Release_MoreThanBooked_Throws()
    {
        var window = Window();
        window.Book(1000);

        Should.Throw<DomainValidationException>(() => window.Release(1001));
    }

    [Theory]
    [InlineData("", 1000)]
    [InlineData("Copenhagen", 0)]
    public void Create_InvalidInput_Throws(string region, int capacity)
    {
        Should.Throw<DomainValidationException>(() => DeliveryWindow.Create(new DateOnly(2026, 1, 20), region, capacity));
    }
}

public class EntityUpdateTests
{
    [Fact]
    public void Update_PrivateCustomer_ChangesPhoneAndNameButIgnoresCompanyFields()
    {
        var customer = TestData.PrivateCustomer();

        customer.Update("+4599999999", "Anna Jensen", "Ignored ApS", "12345678");

        customer.Phone.ShouldBe("+4599999999");
        customer.FullName.ShouldBe("Anna Jensen");
        customer.CompanyName.ShouldBeNull();
        customer.Cvr.ShouldBeNull();
    }

    [Fact]
    public void Update_BusinessCustomer_ChangesCompanyAndCvr()
    {
        var customer = TestData.BusinessCustomer();

        customer.Update("+4599999999", null, "New Name ApS", TestData.OtherValidCvr);

        customer.CompanyName.ShouldBe("New Name ApS");
        customer.Cvr.ShouldBe(CvrNumber.Create(TestData.OtherValidCvr));
        customer.FullName.ShouldBeNull();
    }

    [Fact]
    public void Update_BusinessCustomerWithInvalidCvr_ThrowsAndChangesNothing()
    {
        var customer = TestData.BusinessCustomer();
        var oldPhone = customer.Phone;

        Should.Throw<DomainValidationException>(() => customer.Update("+4599999999", null, "New Name ApS", "12345678"));

        customer.Phone.ShouldBe(oldPhone);
        customer.CompanyName.ShouldBe("Hansen Transport ApS");
    }

    [Fact]
    public void Update_Address_ChangesAllFields()
    {
        var address = TestData.AddressFor(TestData.PrivateCustomer());

        address.Update("Ny Vej 2", "8000", "Aarhus C", 4000, "Gate code 1234");

        (address.Street, address.PostalCode.Value, address.City, address.TankCapacityLitres, address.AccessNotes)
            .ShouldBe(("Ny Vej 2", "8000", "Aarhus C", 4000, "Gate code 1234"));
    }

    [Fact]
    public void Update_AddressWithInvalidPostalCode_ThrowsAndChangesNothing()
    {
        var address = TestData.AddressFor(TestData.PrivateCustomer());

        Should.Throw<DomainValidationException>(() => address.Update("Ny Vej 2", "12", "Aarhus C", 4000, null));

        address.Street.ShouldBe("Vejen 1");
        address.PostalCode.Value.ShouldBe("2100");
    }
}

using System.Net;
using Bunit;
using FuelFlow.Web.Api;
using FuelFlow.Web.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace FuelFlow.Web.UnitTests;

internal static class Samples
{
    public static readonly Guid DieselId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid AddressId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    public static Price Price(bool incVat = true) =>
        new(1000, 10m, 10_000m, 0m, 0m, 0m, 10_000m, 0.25m, 2_500m, 12_500m, incVat);

    public static Address Address(Guid? id = null) => new(id ?? AddressId, "Vejen 1", "2100", "København Ø", 3000, null);

    public static Order Order(string status, Guid? id = null, int litres = 1000) => new(
        id ?? Guid.NewGuid(), DieselId, AddressId, litres, status, Price(), null, null, null, null, TestApp.Now, null);

    public static DispatchOrder Dispatch(string status, Guid? id = null) =>
        new(Order(status, id), "Anna Hansen", "+4512345678", Address(), "Copenhagen", null);

    public static Paged<T> Page<T>(params T[] items) => new(items, 1, 20, items.Length);
}

public class LoginPageTests
{
    [Fact]
    public async Task Submit_CorrectCredentials_SignsInAndGoesToTheStartPage()
    {
        using var app = new TestApp();
        app.Http.On(HttpMethod.Post, "api/auth/login",
            new LoginResponse(Jwt.Create("Customer", TestApp.Now.AddMinutes(60)), "Bearer", TestApp.Now.AddMinutes(60)));
        var cut = app.Render<Login>();

        cut.Find("input[type=email]").Change("anna@example.com");
        cut.Find("input[type=password]").Change("secret-pass");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => app.Services.GetRequiredService<NavigationManager>().Uri.ShouldBe("http://localhost/"), TimeSpan.FromSeconds(5));
        app.Session.Token.ShouldNotBeNull();
        app.Http.Calls(HttpMethod.Post, "api/auth/login").Single().Body.ShouldBe("""{"email":"anna@example.com","password":"secret-pass"}""");
    }

    [Fact]
    public void Submit_WrongPassword_ShowsTheServersMessageAndStaysLoggedOut()
    {
        using var app = new TestApp();
        app.Http.OnProblem(HttpMethod.Post, "api/auth/login", HttpStatusCode.Unauthorized,
            """{"title":"Authentication failed.","status":401,"detail":"Invalid email or password."}""");
        var cut = app.Render<Login>();

        cut.Find("input[type=email]").Change("anna@example.com");
        cut.Find("input[type=password]").Change("nope");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => cut.Find(".alert.error").TextContent.ShouldContain("Invalid email or password."), TimeSpan.FromSeconds(5));
        app.Session.Token.ShouldBeNull();
    }
}

public class RegisterPageTests
{
    [Fact]
    public void TypeChoice_ShowsFullNameForPrivateAndCompanyAndCvrForBusiness()
    {
        using var app = new TestApp();
        var cut = app.Render<Register>();

        cut.Markup.ShouldContain("Full name");
        cut.Markup.ShouldNotContain("CVR");

        cut.FindAll("input[type=radio]")[1].Change(true);

        cut.Markup.ShouldContain("Company name");
        cut.Markup.ShouldContain("CVR number");
        cut.Markup.ShouldNotContain("Full name");
    }

    [Fact]
    public void Submit_BusinessCustomer_SendsCompanyAndCvrWithoutAFullName()
    {
        using var app = new TestApp();
        app.Http.On(HttpMethod.Post, "api/auth/register", new { }, HttpStatusCode.Created);
        var cut = app.Render<Register>();
        cut.FindAll("input[type=radio]")[1].Change(true);

        cut.FindAll("form input:not([type=radio])")[0].Change("Jensen ApS");
        cut.FindAll("form input:not([type=radio])")[1].Change("10150817");
        cut.FindAll("form input:not([type=radio])")[2].Change("+4587654321");
        cut.FindAll("form input:not([type=radio])")[3].Change("kontor@jensen.example");
        cut.FindAll("form input:not([type=radio])")[4].Change("correct-horse");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => app.Http.Calls(HttpMethod.Post, "api/auth/register").ShouldNotBeEmpty(), TimeSpan.FromSeconds(5));
        var body = app.Http.Calls(HttpMethod.Post, "api/auth/register").Single().Body!;
        body.ShouldContain("\"type\":\"Business\"");
        body.ShouldContain("\"companyName\":\"Jensen ApS\"");
        body.ShouldContain("\"cvr\":\"10150817\"");
        body.ShouldContain("\"fullName\":null");
    }

    [Fact]
    public void Submit_ApiRejectsTheEmail_ShowsTheFieldErrors()
    {
        using var app = new TestApp();
        app.Http.OnProblem(HttpMethod.Post, "api/auth/register", HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","status":400,"errors":{"email":["'Email' is not a valid email address."]}}""");
        var cut = app.Render<Register>();

        cut.FindAll("form input:not([type=radio])")[0].Change("Anna");
        cut.FindAll("form input:not([type=radio])")[1].Change("+45");
        cut.FindAll("form input:not([type=radio])")[2].Change("not-an-email");
        cut.FindAll("form input:not([type=radio])")[3].Change("correct-horse");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => cut.Find(".alert.error li").TextContent.ShouldContain("email"), TimeSpan.FromSeconds(5));
    }
}

public class AddressesPageTests
{
    [Fact]
    public void Render_ListsTheCustomersAddresses()
    {
        using var app = new TestApp();
        app.Http.On(HttpMethod.Get, "api/customers/me/addresses", new[] { Samples.Address() });

        var cut = app.Render<Addresses>();

        cut.WaitForAssertion(() => cut.Find("tbody").TextContent.ShouldContain("Vejen 1, 2100 København Ø"), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Add_InvalidPostalCode_ShowsTheFieldErrorFromTheApiAndKeepsTheForm()
    {
        using var app = new TestApp();
        app.Http.On(HttpMethod.Get, "api/customers/me/addresses", Array.Empty<Address>());
        app.Http.OnProblem(HttpMethod.Post, "api/customers/me/addresses", HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","status":400,"errors":{"postalCode":["Postal code must be 4 digits (1000-9999)."]}}""");
        var cut = app.Render<Addresses>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("no delivery address yet"), TimeSpan.FromSeconds(5));

        cut.FindAll("form input")[0].Change("Vejen 1");
        cut.FindAll("form input")[1].Change("12");
        cut.FindAll("form input")[2].Change("København");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => cut.Find(".alert.error li").TextContent.ShouldContain("Postal code must be 4 digits"), TimeSpan.FromSeconds(5));
        cut.FindAll("form input")[1].GetAttribute("value").ShouldBe("12");
    }

    [Fact]
    public void Delete_AddressWithOrders_ShowsTheConflictMessage()
    {
        using var app = new TestApp();
        app.Http.On(HttpMethod.Get, "api/customers/me/addresses", new[] { Samples.Address() });
        app.Http.OnProblem(HttpMethod.Delete, "api/customers/me/addresses", HttpStatusCode.Conflict,
            """{"title":"Conflict.","status":409,"detail":"The address has orders and cannot be deleted."}""");
        var cut = app.Render<Addresses>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Vejen 1"), TimeSpan.FromSeconds(5));

        cut.Find("button.danger").Click();

        cut.WaitForAssertion(() => cut.Find(".alert.error").TextContent.ShouldContain("cannot be deleted"), TimeSpan.FromSeconds(5));
    }
}

public class NewOrderPageTests
{
    private static IRenderedComponent<NewOrder> RenderReady(TestApp app, bool incVat = true)
    {
        app.Http.On(HttpMethod.Get, "api/products", new[] { new Product(Samples.DieselId, "Diesel", 10m, 12.5m) });
        app.Http.On(HttpMethod.Get, "api/customers/me/addresses", new[] { Samples.Address() });
        app.Http.On(HttpMethod.Post, "api/quotes", Samples.Price(incVat));
        var cut = app.Render<NewOrder>();
        cut.WaitForAssertion(() => cut.FindAll("select").Count.ShouldBe(2), TimeSpan.FromSeconds(5));
        return cut;
    }

    private static void FillAndQuote(IRenderedComponent<NewOrder> cut, int litres = 1000)
    {
        cut.FindAll("select")[0].Change(Samples.DieselId.ToString());
        cut.FindAll("select")[1].Change(Samples.AddressId.ToString());
        cut.Find("input[type=number]").Change(litres.ToString());
        cut.Find("form").Submit();
    }

    [Fact]
    public void Render_NoAddress_ExplainsAnAddressIsNeededFirst()
    {
        using var app = new TestApp();
        app.Http.On(HttpMethod.Get, "api/products", Array.Empty<Product>());
        app.Http.On(HttpMethod.Get, "api/customers/me/addresses", Array.Empty<Address>());

        var cut = app.Render<NewOrder>();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("You need a delivery address first"), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void GetPrice_PrivateCustomer_HighlightsTheTotalIncludingVat()
    {
        using var app = new TestApp();
        var cut = RenderReady(app, incVat: true);

        FillAndQuote(cut);

        cut.WaitForAssertion(() => cut.Find("tr.highlight").TextContent.ShouldContain("Total incl. VAT"), TimeSpan.FromSeconds(5));
        cut.Find("tr.highlight").TextContent.ShouldContain("12.500,00 kr.");
        app.Http.Calls(HttpMethod.Post, "api/quotes").Single().Body
            .ShouldBe($$"""{"productId":"{{Samples.DieselId}}","deliveryAddressId":"{{Samples.AddressId}}","litres":1000}""");
    }

    [Fact]
    public void GetPrice_BusinessCustomer_HighlightsTheTotalExcludingVat()
    {
        using var app = new TestApp();
        var cut = RenderReady(app, incVat: false);

        FillAndQuote(cut);

        cut.WaitForAssertion(() => cut.Find("tr.highlight").TextContent.ShouldContain("Total excl. VAT"), TimeSpan.FromSeconds(5));
        cut.Find("tr.highlight").TextContent.ShouldContain("10.000,00 kr.");
    }

    [Fact]
    public void GetPrice_BelowTheMinimum_ShowsTheApiMessageAndNoPrice()
    {
        using var app = new TestApp();
        app.Http.On(HttpMethod.Get, "api/products", new[] { new Product(Samples.DieselId, "Diesel", 10m, 12.5m) });
        app.Http.On(HttpMethod.Get, "api/customers/me/addresses", new[] { Samples.Address() });
        app.Http.OnProblem(HttpMethod.Post, "api/quotes", HttpStatusCode.BadRequest,
            """{"title":"The request breaks a business rule.","status":400,"detail":"Minimum order is 500 litres."}""");
        var cut = app.Render<NewOrder>();
        cut.WaitForAssertion(() => cut.FindAll("select").Count.ShouldBe(2), TimeSpan.FromSeconds(5));

        FillAndQuote(cut, litres: 100);

        cut.WaitForAssertion(() => cut.Find(".alert.error").TextContent.ShouldContain("Minimum order is 500 litres."), TimeSpan.FromSeconds(5));
        cut.FindAll("table.price").ShouldBeEmpty();
    }

    [Fact]
    public void ChangingTheLitresAfterAQuote_HidesTheOldPrice()
    {
        using var app = new TestApp();
        var cut = RenderReady(app);
        FillAndQuote(cut);
        cut.WaitForAssertion(() => cut.FindAll("table.price").Count.ShouldBe(1), TimeSpan.FromSeconds(5));

        cut.Find("input[type=number]").Change("2000");

        cut.FindAll("table.price").ShouldBeEmpty();
    }

    [Fact]
    public void PlaceOrder_AfterAQuote_PostsTheSameRequestAndGoesToMyOrders()
    {
        using var app = new TestApp();
        app.Http.On(HttpMethod.Post, "api/orders", Samples.Order("Pending"), HttpStatusCode.Created);
        var cut = RenderReady(app);
        FillAndQuote(cut, litres: 800);
        cut.WaitForAssertion(() => cut.FindAll("table.price").Count.ShouldBe(1), TimeSpan.FromSeconds(5));

        cut.Find("section button").Click();

        cut.WaitForAssertion(() => app.Services.GetRequiredService<NavigationManager>().Uri.ShouldBe("http://localhost/orders"), TimeSpan.FromSeconds(5));
        app.Http.Calls(HttpMethod.Post, "api/orders").Single().Body!.ShouldContain("\"litres\":800");
    }
}

public class MyOrdersPageTests
{
    [Fact]
    public void Render_OnlyCancellableOrdersHaveACancelButton_AndDeliveredOrdersShowTheFinalPrice()
    {
        using var app = new TestApp();
        var delivered = Samples.Order("Delivered") with { FinalPrice = Samples.Price() with { TotalIncVat = 11_875m }, DeliveredLitres = 950 };
        app.Http.On(HttpMethod.Get, "api/orders", Samples.Page(Samples.Order("Pending"), Samples.Order("OutForDelivery"), delivered));

        var cut = app.Render<MyOrders>();

        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(3), TimeSpan.FromSeconds(5));
        cut.FindAll("button.link.danger").Count.ShouldBe(1);
        var deliveredRow = cut.FindAll("tbody tr")[2].TextContent;
        deliveredRow.ShouldContain("11.875,00 kr.");
        deliveredRow.ShouldContain("950 L");
    }

    [Fact]
    public void Cancel_NeedsAReasonThenPostsItAndReloads()
    {
        using var app = new TestApp();
        var order = Samples.Order("Pending");
        app.Http.On(HttpMethod.Get, "api/orders", Samples.Page(order));
        app.Http.On(HttpMethod.Post, $"api/orders/{order.Id}/cancel", Samples.Order("Cancelled", order.Id));
        var cut = app.Render<MyOrders>();
        cut.WaitForAssertion(() => cut.FindAll("button.link.danger").Count.ShouldBe(1), TimeSpan.FromSeconds(5));

        cut.Find("button.link.danger").Click();
        cut.Find("button.danger").HasAttribute("disabled").ShouldBeTrue("a reason is required");
        cut.Find("td.actions input").Input("Ordered by mistake");
        cut.Find("button.danger").Click();

        cut.WaitForAssertion(() => app.Http.Calls(HttpMethod.Post, $"api/orders/{order.Id}/cancel").ShouldNotBeEmpty(), TimeSpan.FromSeconds(5));
        app.Http.Calls(HttpMethod.Post, $"api/orders/{order.Id}/cancel").Single().Body.ShouldBe("""{"reason":"Ordered by mistake"}""");
        app.Http.Calls(HttpMethod.Get, "api/orders").Count().ShouldBe(2);
    }

    [Fact]
    public void Render_NoOrders_InvitesTheCustomerToPlaceOne()
    {
        using var app = new TestApp();
        app.Http.On(HttpMethod.Get, "api/orders", new Paged<Order>([], 1, 10, 0));

        var cut = app.Render<MyOrders>();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Place your first order"), TimeSpan.FromSeconds(5));
    }
}

public class DispatchPageTests
{
    private static IRenderedComponent<Dispatch> RenderWith(TestApp app, params DispatchOrder[] orders)
    {
        app.Http.On(HttpMethod.Get, "api/dispatch/regions", new[] { new Region("Copenhagen", 1000, 2999), new Region("Aarhus", 7000, 8999) });
        app.Http.On(HttpMethod.Get, "api/dispatch/orders", Samples.Page(orders));
        var cut = app.Render<Dispatch>();
        cut.WaitForAssertion(() => cut.FindAll("tbody tr").Count.ShouldBe(orders.Length), TimeSpan.FromSeconds(5));
        return cut;
    }

    [Fact]
    public void Render_EachStatusOffersTheRightAction()
    {
        using var app = new TestApp();

        var cut = RenderWith(app, Samples.Dispatch("Pending"), Samples.Dispatch("Confirmed"), Samples.Dispatch("Scheduled"),
            Samples.Dispatch("OutForDelivery"), Samples.Dispatch("Delivered"), Samples.Dispatch("Cancelled"));

        var actions = cut.FindAll("td.actions").Select(td => td.TextContent.Trim()).ToList();
        actions[0].ShouldBe("Confirm");
        actions[1].ShouldBe("Waiting for a delivery window");
        actions[2].ShouldBe("Out for delivery");
        actions[3].ShouldBe("Delivered");
        actions[4].ShouldBeEmpty();
        actions[5].ShouldBeEmpty();
    }

    [Fact]
    public void Render_ShowsCustomerContactAddressAndRegion()
    {
        using var app = new TestApp();

        var cut = RenderWith(app, Samples.Dispatch("Pending"));

        var row = cut.Find("tbody tr").TextContent;
        row.ShouldContain("Anna Hansen");
        row.ShouldContain("+4512345678");
        row.ShouldContain("Vejen 1, 2100 København Ø");
        row.ShouldContain("Copenhagen");
        row.ShouldContain("Not planned");
    }

    [Fact]
    public void Confirm_CallsTheApiAndReloadsTheList()
    {
        using var app = new TestApp();
        var order = Samples.Dispatch("Pending");
        app.Http.On(HttpMethod.Post, $"api/dispatch/orders/{order.Order.Id}/confirm", Samples.Order("Confirmed", order.Order.Id));
        var cut = RenderWith(app, order);

        cut.Find("td.actions button").Click();

        cut.WaitForAssertion(() => app.Http.Calls(HttpMethod.Post, $"api/dispatch/orders/{order.Order.Id}/confirm").Count().ShouldBe(1), TimeSpan.FromSeconds(5));
        cut.WaitForAssertion(() => app.Http.Calls(HttpMethod.Get, "api/dispatch/orders").Count().ShouldBe(2), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Deliver_AsksForLitresPrefilledWithTheOrderedAmountAndPostsTheEnteredValue()
    {
        using var app = new TestApp();
        var order = Samples.Dispatch("OutForDelivery");
        app.Http.On(HttpMethod.Post, $"api/dispatch/orders/{order.Order.Id}/deliver", Samples.Order("Delivered", order.Order.Id));
        var cut = RenderWith(app, order);

        cut.Find("td.actions button").Click();
        cut.Find("td.actions input").GetAttribute("value").ShouldBe("1000");
        cut.Find("td.actions input").Change("950");
        cut.FindAll("td.actions button")[0].Click();

        cut.WaitForAssertion(() => app.Http.Calls(HttpMethod.Post, $"api/dispatch/orders/{order.Order.Id}/deliver").ShouldNotBeEmpty(), TimeSpan.FromSeconds(5));
        app.Http.Calls(HttpMethod.Post, $"api/dispatch/orders/{order.Order.Id}/deliver").Single().Body.ShouldBe("""{"deliveredLitres":950}""");
    }

    [Fact]
    public void Deliver_ApiRefusesTheAmount_ShowsTheMessage()
    {
        using var app = new TestApp();
        var order = Samples.Dispatch("OutForDelivery");
        app.Http.OnProblem(HttpMethod.Post, $"api/dispatch/orders/{order.Order.Id}/deliver", HttpStatusCode.BadRequest,
            """{"title":"The request breaks a business rule.","status":400,"detail":"Delivered litres must be within 10 % of the ordered 1000 litres."}""");
        var cut = RenderWith(app, order);

        cut.Find("td.actions button").Click();
        cut.Find("td.actions input").Change("5000");
        cut.FindAll("td.actions button")[0].Click();

        cut.WaitForAssertion(() => cut.Find(".alert.error").TextContent.ShouldContain("within 10 %"), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Confirm_SomeoneElseChangedTheOrderFirst_ShowsTheConflict()
    {
        using var app = new TestApp();
        var order = Samples.Dispatch("Pending");
        app.Http.OnProblem(HttpMethod.Post, $"api/dispatch/orders/{order.Order.Id}/confirm", HttpStatusCode.Conflict,
            """{"title":"Conflict.","status":409,"detail":"The data was changed by someone else at the same time. Reload and try again."}""");
        var cut = RenderWith(app, order);

        cut.Find("td.actions button").Click();

        cut.WaitForAssertion(() => cut.Find(".alert.error").TextContent.ShouldContain("changed by someone else"), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Filter_StatusRegionAndDate_AreSentToTheApiAndPagingStartsAtOne()
    {
        using var app = new TestApp();
        var cut = RenderWith(app, Samples.Dispatch("Pending"));

        cut.FindAll("select")[0].Change("Scheduled");
        cut.FindAll("select")[1].Change("Aarhus");
        cut.Find("input[type=date]").Change("2026-03-05");
        cut.Find("form.filters").Submit();

        cut.WaitForAssertion(() => app.Http.Calls(HttpMethod.Get, "api/dispatch/orders").Count().ShouldBe(2), TimeSpan.FromSeconds(5));
        app.Http.Calls(HttpMethod.Get, "api/dispatch/orders").Last().PathAndQuery
            .ShouldBe("api/dispatch/orders?page=1&pageSize=20&status=Scheduled&region=Aarhus&date=2026-03-05");
        cut.FindAll("select")[1].InnerHtml.ShouldContain("Aarhus");
    }
}

using System.Net;
using Bunit;
using FuelFlow.Web.Api;
using Shouldly;

namespace FuelFlow.Web.UnitTests;

public class JwtClaimsTests
{
    private static readonly DateTimeOffset _now = TestApp.Now;

    [Fact]
    public void ToPrincipal_ValidToken_ReadsRoleAndEmailEvenWithBase64UrlCharacters()
    {
        var principal = JwtClaims.ToPrincipal(Jwt.Create("Dispatcher", _now.AddMinutes(10)), _now);

        principal.Identity!.IsAuthenticated.ShouldBeTrue();
        principal.Identity.Name.ShouldBe("anna@example.com");
        principal.IsInRole("Dispatcher").ShouldBeTrue();
        principal.IsInRole("Admin").ShouldBeFalse();
    }

    [Fact]
    public void ToPrincipal_ExpiredToken_IsAnonymous()
    {
        JwtClaims.ToPrincipal(Jwt.Create("Customer", _now.AddSeconds(-1)), _now).Identity!.IsAuthenticated.ShouldBeFalse();
        JwtClaims.ToPrincipal(Jwt.Create("Customer", _now), _now).Identity!.IsAuthenticated.ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("a.b.c")]
    [InlineData("a.e30.c")] // payload "{}" has no expiry
    public void ToPrincipal_MissingOrBrokenToken_IsAnonymous(string? token)
    {
        JwtClaims.ToPrincipal(token, _now).Identity!.IsAuthenticated.ShouldBeFalse();
    }
}

public class FormatTests
{
    [Fact]
    public void Money_And_Litres_UseDanishSeparators()
    {
        Format.Money(12_500.5m).ShouldBe("12.500,50 kr.");
        Format.Money(0.07m).ShouldBe("0,07 kr.");
        Format.Litres(10_000).ShouldBe("10.000 L");
    }

    [Fact]
    public void Date_DateOnly_UsesDayMonthYear() => Format.Date(new DateOnly(2026, 3, 5)).ShouldBe("05.03.2026");
}

public class AuthSessionTests
{
    [Fact]
    public async Task SignInAsync_ValidToken_BecomesAuthenticatedAndStoresTokenInSessionStorage()
    {
        using var app = new TestApp();

        await app.SignInAsync("Customer");

        var user = (await app.Session.GetAuthenticationStateAsync()).User;
        user.IsInRole("Customer").ShouldBeTrue();
        app.JSInterop.Invocations["sessionStorage.setItem"].Count.ShouldBe(1);
    }

    [Fact]
    public async Task SignOutAsync_ClearsTokenAndStorage()
    {
        using var app = new TestApp();
        await app.SignInAsync();

        await app.Session.SignOutAsync();

        app.Session.Token.ShouldBeNull();
        (await app.Session.GetAuthenticationStateAsync()).User.Identity!.IsAuthenticated.ShouldBeFalse();
        app.JSInterop.Invocations["sessionStorage.removeItem"].Count.ShouldBe(1);
    }

    [Fact]
    public async Task GetAuthenticationStateAsync_FirstCall_RestoresTokenFromSessionStorage()
    {
        using var app = new TestApp();
        app.JSInterop.Setup<string?>("sessionStorage.getItem", _ => true).SetResult(Jwt.Create("Admin", TestApp.Now.AddMinutes(5)));

        var user = (await app.Session.GetAuthenticationStateAsync()).User;

        user.IsInRole("Admin").ShouldBeTrue();
    }
}

public class ApiClientTests
{
    [Fact]
    public async Task Requests_WhenLoggedIn_CarryTheBearerToken()
    {
        using var app = new TestApp();
        app.Http.On(HttpMethod.Get, "api/products", Array.Empty<Product>());
        await app.SignInAsync();

        await app.Api.GetProductsAsync();

        app.Http.Requests.Single().Authorization.ShouldBe($"Bearer {app.Session.Token}");
    }

    [Fact]
    public async Task Requests_WhenLoggedOut_HaveNoAuthorizationHeader()
    {
        using var app = new TestApp();
        app.Http.On(HttpMethod.Get, "api/products", Array.Empty<Product>());

        await app.Api.GetProductsAsync();

        app.Http.Requests.Single().Authorization.ShouldBeNull();
    }

    [Fact]
    public async Task ErrorAnswer_WithFieldErrors_BecomesApiExceptionListingTheFields()
    {
        using var app = new TestApp();
        app.Http.OnProblem(HttpMethod.Post, "api/customers/me/addresses", HttpStatusCode.BadRequest,
            """{"title":"One or more validation errors occurred.","status":400,"errors":{"postalCode":["Postal code must be 4 digits (1000-9999)."]}}""");

        var ex = await Should.ThrowAsync<ApiException>(() => app.Api.CreateAddressAsync(new AddressRequest("Vejen 1", "12", "By", 3000, null)));

        ex.Status.ShouldBe(400);
        ex.Message.ShouldBe("One or more validation errors occurred.");
        ex.FieldErrors["postalCode"].ShouldBe(["Postal code must be 4 digits (1000-9999)."]);
    }

    [Fact]
    public async Task ErrorAnswer_WithDetail_UsesTheDetailAsTheMessage()
    {
        using var app = new TestApp();
        app.Http.OnProblem(HttpMethod.Post, "api/dispatch/orders", HttpStatusCode.Conflict,
            """{"title":"Conflict.","status":409,"detail":"Order cannot move from Pending to Delivered."}""");

        var ex = await Should.ThrowAsync<ApiException>(() => app.Api.ConfirmAsync(Guid.NewGuid()));

        (ex.Status, ex.Message).ShouldBe((409, "Order cannot move from Pending to Delivered."));
    }

    [Fact]
    public async Task ErrorAnswer_WithoutProblemBody_StillGivesAUsefulMessage()
    {
        using var app = new TestApp();
        app.Http.OnProblem(HttpMethod.Get, "api/products", HttpStatusCode.BadGateway, "<html>502</html>");

        var ex = await Should.ThrowAsync<ApiException>(() => app.Api.GetProductsAsync());

        (ex.Status, ex.Message).ShouldBe((502, "The request failed (502)."));
    }

    [Fact]
    public async Task Unauthorized_WhileLoggedIn_SignsOutAndExplainsTheSessionExpired()
    {
        using var app = new TestApp();
        app.Http.OnProblem(HttpMethod.Get, "api/orders", HttpStatusCode.Unauthorized, """{"title":"Unauthorized","status":401}""");
        await app.SignInAsync();

        var ex = await Should.ThrowAsync<ApiException>(() => app.Api.GetOrdersAsync(1, 10));

        ex.Message.ShouldContain("session has expired");
        app.Session.Token.ShouldBeNull();
    }

    [Fact]
    public async Task Unauthorized_OnLogin_KeepsTheServersMessageAndDoesNotTouchTheSession()
    {
        using var app = new TestApp();
        app.Http.OnProblem(HttpMethod.Post, "api/auth/login", HttpStatusCode.Unauthorized,
            """{"title":"Authentication failed.","status":401,"detail":"Invalid email or password."}""");

        var ex = await Should.ThrowAsync<ApiException>(() => app.Api.LoginAsync("a@b.dk", "wrong"));

        ex.Message.ShouldBe("Invalid email or password.");
    }

    [Fact]
    public async Task NetworkFailure_BecomesApiExceptionWithStatusZero()
    {
        using var app = new TestApp();
        app.Http.OnNetworkFailure(HttpMethod.Get, "api/products");

        var ex = await Should.ThrowAsync<ApiException>(() => app.Api.GetProductsAsync());

        (ex.Status, ex.Message).ShouldBe((0, "Cannot reach the server. Check your connection and try again."));
    }

    [Fact]
    public async Task GetDispatchOrdersAsync_OnlyAddsTheFiltersThatAreSet()
    {
        using var app = new TestApp();
        app.Http.On(HttpMethod.Get, "api/dispatch/orders", new Paged<DispatchOrder>([], 1, 20, 0));

        await app.Api.GetDispatchOrdersAsync("Scheduled", "", null, 2, 20);
        await app.Api.GetDispatchOrdersAsync(null, "South Jutland", "2026-03-05", 1, 20);

        app.Http.Requests[0].PathAndQuery.ShouldBe("api/dispatch/orders?page=2&pageSize=20&status=Scheduled");
        app.Http.Requests[1].PathAndQuery.ShouldBe("api/dispatch/orders?page=1&pageSize=20&region=South%20Jutland&date=2026-03-05");
    }
}

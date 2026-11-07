using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FuelFlow.Application.Auth;
using FuelFlow.Domain.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Shouldly;

namespace FuelFlow.Api.IntegrationTests;

public class AuthTests(FuelFlowApiFactory factory) : IntegrationTestBase(factory)
{
    private static string Password => "correct-horse-battery";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object PrivateRegistration(string email = "anna@example.com") => new
    {
        email,
        password = Password,
        type = "Private",
        phone = "+4512345678",
        fullName = "Anna Hansen",
    };

    private async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        await client.PostAsJsonAsync("/api/auth/login", new { email, password }, Ct);

    private async Task<string> RegisterAndLoginAsync(HttpClient client, string email = "anna@example.com")
    {
        (await client.PostAsJsonAsync("/api/auth/register", PrivateRegistration(email), Ct)).EnsureSuccessStatusCode();
        var response = await LoginAsync(client, email, Password);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginResponse>(Ct))!.AccessToken;
    }

    private async Task<string> CreateStaffAndLoginAsync(HttpClient client, UserRole role)
    {
        await using (var scope = NewScope())
        {
            var hash = scope.ServiceProvider.GetRequiredService<IPasswordService>().Hash(Password);
            Db(scope).Users.Add(User.Create("staff@example.com", hash, role));
            await Db(scope).SaveChangesAsync(Ct);
        }

        var response = await LoginAsync(client, "staff@example.com", Password);
        return (await response.Content.ReadFromJsonAsync<LoginResponse>(Ct))!.AccessToken;
    }

    private static HttpRequestMessage Get(string url, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    [Fact]
    public async Task Register_PrivateCustomer_Returns201AndStoresHashedPassword()
    {
        using var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", PrivateRegistration("Anna@Example.com"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = (await response.Content.ReadFromJsonAsync<RegisterResponse>(Ct))!;
        await using var scope = NewScope();
        var user = await Db(scope).Users.SingleAsync(Ct);
        user.Id.ShouldBe(body.UserId);
        user.CustomerId.ShouldBe(body.CustomerId);
        user.Role.ShouldBe(UserRole.Customer);
        user.Email.ShouldBe("anna@example.com");
        user.PasswordHash.ShouldNotContain(Password);
    }

    [Fact]
    public async Task Register_BusinessCustomer_StoresCompanyAndCvr()
    {
        using var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = "kontor@jensen.example",
            password = Password,
            type = "Business",
            phone = "+4587654321",
            companyName = "Jensen Transport ApS",
            cvr = "10150817",
        }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        await using var scope = NewScope();
        var customer = await Db(scope).Customers.SingleAsync(Ct);
        customer.CompanyName.ShouldBe("Jensen Transport ApS");
        customer.Cvr!.Value.Value.ShouldBe("10150817");
    }

    [Fact]
    public async Task Register_DuplicateEmailDifferentCase_Returns409ProblemDetails()
    {
        using var client = Factory.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/register", PrivateRegistration("anna@example.com"), Ct)).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/auth/register", PrivateRegistration("ANNA@example.com"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        await using var scope = NewScope();
        (await Db(scope).Customers.CountAsync(Ct)).ShouldBe(1, "the failed registration must not leave a customer behind");
    }

    [Fact]
    public async Task Register_InvalidInput_Returns400ListingFieldErrors()
    {
        using var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = "not-an-email",
            password = "short",
            type = "Business",
            phone = "",
            companyName = "",
            cvr = "12345678",
        }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var problem = (await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>(Ct))!;
        problem.Errors.Keys.ShouldBe(["email", "password", "phone", "companyName", "cvr"], ignoreOrder: true);
    }

    [Fact]
    public async Task Login_CorrectCredentials_ReturnsTokenWithIdentityClaimsExpiringInOneHour()
    {
        using var client = Factory.CreateClient();
        var registered = await (await client.PostAsJsonAsync("/api/auth/register", PrivateRegistration(), Ct))
            .Content.ReadFromJsonAsync<RegisterResponse>(Ct);

        var response = await LoginAsync(client, "anna@example.com", Password);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var login = (await response.Content.ReadFromJsonAsync<LoginResponse>(Ct))!;
        login.TokenType.ShouldBe("Bearer");
        login.ExpiresAt.ShouldBe(FuelFlowApiFactory.StartTime.AddMinutes(60));

        var token = new JsonWebTokenHandler().ReadJsonWebToken(login.AccessToken);
        token.GetClaim("sub").Value.ShouldBe(registered!.UserId.ToString());
        token.GetClaim("role").Value.ShouldBe("Customer");
        token.GetClaim("customer_id").Value.ShouldBe(registered.CustomerId.ToString());
        token.ValidTo.ShouldBe(FuelFlowApiFactory.StartTime.AddMinutes(60).UtcDateTime);
        token.TryGetPayloadValue<long>("nbf", out _).ShouldBeFalse("no not-before claim, so clock drift between replicas cannot reject a fresh token");
    }

    [Fact]
    public async Task Login_WrongPasswordOrUnknownEmail_Returns401WithSameProblem()
    {
        using var client = Factory.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/register", PrivateRegistration(), Ct)).EnsureSuccessStatusCode();

        var wrongPassword = await LoginAsync(client, "anna@example.com", "wrong-password");
        var unknownEmail = await LoginAsync(client, "nobody@example.com", Password);

        wrongPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        unknownEmail.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        wrongPassword.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");

        // Same answer for both, so the API does not reveal which emails are registered (traceId differs per request).
        var a = (await wrongPassword.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        var b = (await unknownEmail.Content.ReadFromJsonAsync<ProblemDetails>(Ct))!;
        (a.Title, a.Detail, a.Status).ShouldBe((b.Title, b.Detail, b.Status));
    }

    [Fact]
    public async Task Me_WithoutToken_Returns401ProblemDetails()
    {
        using var client = Factory.CreateClient();

        var response = await client.SendAsync(Get("/api/auth/me", token: null), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task Me_WithValidToken_ReturnsIdentityFromClaims()
    {
        using var client = Factory.CreateClient();
        var token = await RegisterAndLoginAsync(client);

        var response = await client.SendAsync(Get("/api/auth/me", token), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var me = (await response.Content.ReadFromJsonAsync<Controllers.MeResponse>(Ct))!;
        me.Email.ShouldBe("anna@example.com");
        me.Role.ShouldBe("Customer");
        me.CustomerId.ShouldNotBeNull();
    }

    [Fact]
    public async Task Me_WithExpiredToken_Returns401()
    {
        using var client = Factory.CreateClient();
        var token = await RegisterAndLoginAsync(client);

        Factory.Time.Advance(TimeSpan.FromMinutes(59));
        (await client.SendAsync(Get("/api/auth/me", token), Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);

        Factory.Time.Advance(TimeSpan.FromMinutes(2));
        (await client.SendAsync(Get("/api/auth/me", token), Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_WithTamperedToken_Returns401()
    {
        using var client = Factory.CreateClient();
        var token = await RegisterAndLoginAsync(client);
        var tampered = token[..^3] + (token[^3] == 'a' ? "bbb" : "aaa");

        var response = await client.SendAsync(Get("/api/auth/me", tampered), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RoleProtectedEndpoint_CustomerToken_Returns403AndAdminToken_Returns200()
    {
        using var client = Factory.CreateClient();
        var customerToken = await RegisterAndLoginAsync(client);
        var adminToken = await CreateStaffAndLoginAsync(client, UserRole.Admin);

        var asCustomer = await client.SendAsync(Get("/test/admin-only", customerToken), Ct);
        var asAdmin = await client.SendAsync(Get("/test/admin-only", adminToken), Ct);
        var anonymous = await client.SendAsync(Get("/test/admin-only", token: null), Ct);

        asCustomer.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        asCustomer.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        asAdmin.StatusCode.ShouldBe(HttpStatusCode.OK);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}

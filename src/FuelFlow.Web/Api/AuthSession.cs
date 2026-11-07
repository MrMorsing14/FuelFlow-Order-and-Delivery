using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace FuelFlow.Web.Api;

/// <summary>
/// Holds the login token and tells Blazor who is logged in (like Spring Security's SecurityContext).
/// The token lives in memory and in sessionStorage, so a page reload in the same tab keeps you logged in.
/// sessionStorage is readable by any script on the page (see the trade-off in the README); it is emptied when the tab closes.
/// </summary>
public sealed class AuthSession(IJSRuntime js, TimeProvider timeProvider) : AuthenticationStateProvider
{
    private static string StorageKey => "fuelflow.token";

    private string? _token;
    private bool _loaded;

    public string? Token => _token;

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (!_loaded)
        {
            _token = await js.InvokeAsync<string?>("sessionStorage.getItem", StorageKey);
            _loaded = true;
        }

        return new AuthenticationState(CurrentUser());
    }

    public async Task SignInAsync(string token)
    {
        _token = token;
        _loaded = true;
        await js.InvokeVoidAsync("sessionStorage.setItem", StorageKey, token);
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(CurrentUser())));
    }

    public async Task SignOutAsync()
    {
        _token = null;
        _loaded = true;
        await js.InvokeVoidAsync("sessionStorage.removeItem", StorageKey);
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(JwtClaims.Anonymous)));
    }

    private ClaimsPrincipal CurrentUser() => JwtClaims.ToPrincipal(_token, timeProvider.GetUtcNow());
}

using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Options;

namespace HomeDutiesAssistant.Web.Auth;

// Reads the JWT from the HttpOnly cookie during SSR/prerender (HttpContext is
// available then), validates it, and persists the resulting claims so the
// interactive circuit — a fresh scope with no HttpContext — can restore them.
public sealed class CookieJwtAuthenticationStateProvider : AuthenticationStateProvider, IDisposable
{
    private const string PersistKey = "authentication-state";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly JwtTokenService _jwtTokenService;
    private readonly PersistentComponentState _persistentState;
    private readonly string _cookieName;
    private readonly PersistingComponentStateSubscription _persistingSubscription;
    private AuthenticationState? _cachedState;

    public CookieJwtAuthenticationStateProvider(
        IHttpContextAccessor httpContextAccessor, JwtTokenService jwtTokenService,
        PersistentComponentState persistentState, IOptions<Auth> options)
    {
        _httpContextAccessor = httpContextAccessor;
        _jwtTokenService = jwtTokenService;
        _persistentState = persistentState;
        _cookieName = options.Value.CookieName;
        _persistingSubscription = _persistentState.RegisterOnPersisting(PersistAsync, RenderMode.InteractiveServer);
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        _cachedState ??= new AuthenticationState(ResolvePrincipal());
        return Task.FromResult(_cachedState);
    }

    private ClaimsPrincipal ResolvePrincipal()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext is not null)
        {
            if (httpContext.Request.Cookies.TryGetValue(_cookieName, out var token) && !string.IsNullOrEmpty(token))
            {
                var principal = _jwtTokenService.Validate(token);
                if (principal is not null) return principal;
            }
            return Anonymous;
        }

        if (_persistentState.TryTakeFromJson<PersistedClaim[]>(PersistKey, out var claims) && claims is { Length: > 0 })
        {
            var identity = new ClaimsIdentity(
                claims.Select(claim => new Claim(claim.Type, claim.Value)),
                authenticationType: "jwt",
                nameType: JwtTokenService.NameClaim,
                roleType: JwtTokenService.RoleClaim);
            return new ClaimsPrincipal(identity);
        }

        return Anonymous;
    }

    private Task PersistAsync()
    {
        var user = _cachedState?.User;
        if (user?.Identity?.IsAuthenticated == true)
            _persistentState.PersistAsJson(PersistKey,
                user.Claims.Select(claim => new PersistedClaim(claim.Type, claim.Value)).ToArray());
        return Task.CompletedTask;
    }

    private static ClaimsPrincipal Anonymous => new(new ClaimsIdentity());

    public void Dispose() => _persistingSubscription.Dispose();

    private sealed record PersistedClaim(string Type, string Value);
}
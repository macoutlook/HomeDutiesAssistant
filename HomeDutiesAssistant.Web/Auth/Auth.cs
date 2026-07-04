namespace HomeDutiesAssistant.Web.Auth;

// Bound from the "Auth" section. SigningKey is a secret — supply via environment
// variables / user-secrets in production.
public sealed class Auth
{
    public const string SectionName = nameof(Auth);

    public JwtOptions Jwt { get; set; } = new();
    public string CookieName { get; set; } = "HomeDutiesAssistant.Auth";
}

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "HomeDutiesAssistant";
    public string Audience { get; set; } = "HomeDutiesAssistant.Web";
    public string SigningKey { get; set; } = "";
    public int LifetimeMinutes { get; set; } = 480;
}
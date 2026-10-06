using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HomeDutiesAssistant.Web.Auth;

public sealed class JwtTokenService
{
    public const string NameClaim = "name";
    public const string RoleClaim = "role";
    public const string HomeClaim = "home";

    private readonly JwtOptions _options;
    private readonly SymmetricSecurityKey _key;
    private readonly TokenValidationParameters _validationParameters;
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };

    public JwtTokenService(IOptions<Auth> options)
    {
        _options = options.Value.Jwt;
        if (string.IsNullOrWhiteSpace(_options.SigningKey) || Encoding.UTF8.GetByteCount(_options.SigningKey) < 32)
            throw new InvalidOperationException(
                "Auth.Jwt.SigningKey must be set and at least 32 bytes (256 bits).");
        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        _validationParameters = CreateValidationParameters(_options);
    }

    // Shared by Validate() and the JwtBearer scheme so both agree on issuer,
    // audience, key, and the name/role claim types.
    public static TokenValidationParameters CreateValidationParameters(JwtOptions options) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = options.Issuer,
        ValidateAudience = true,
        ValidAudience = options.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = NameClaim,
        RoleClaimType = RoleClaim,
    };

    public string Create(string userName, string role, long? homeId)
    {
        var now = DateTime.UtcNow;
        var claims = new List<Claim>
        {
            new(NameClaim, userName),
            new(RoleClaim, role),
            new(JwtRegisteredClaimNames.Iat,
                new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        if (homeId is not null)
            claims.Add(new Claim(HomeClaim, homeId.Value.ToString()));

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: now.AddMinutes(_options.LifetimeMinutes),
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));

        return _handler.WriteToken(token);
    }

    public ClaimsPrincipal? Validate(string token)
    {
        try
        {
            return _handler.ValidateToken(token, _validationParameters, out _);
        }
        catch
        {
            return null;
        }
    }

    // The home the user belongs to, from the `home` claim; null if none assigned.
    public static long? HomeId(ClaimsPrincipal user)
        => long.TryParse(user.FindFirst(HomeClaim)?.Value, out var id) ? id : null;
}
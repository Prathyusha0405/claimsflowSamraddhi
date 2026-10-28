using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClaimFlow.Contracts;
using Microsoft.Extensions.Options;

namespace ClaimFlow.Platform.Security;

public sealed class JwtOptions
{
    public string Secret { get; set; } = "claimflow-dev-secret-please-change-me-0123456789abcdef";
    public string Issuer { get; set; } = "claimflow-identity";
    public string Audience { get; set; } = "claimflow";
    public int ExpiryMinutes { get; set; } = 480;
}

public sealed record AuthUser(string Id, string Email, string Name, string Role, Market? Market, decimal AuthorityLimitUsd);

/// <summary>The caller as seen by a service, extracted from a validated token.</summary>
public sealed record CurrentUser(string Id, string Name, string Role, Market? Market, decimal AuthorityLimitUsd)
{
    public bool IsStaff => Role is Roles.ClaimsOfficer or Roles.Manager;
    public bool IsManager => Role == Roles.Manager;
    public bool IsClaimant => Role == Roles.Claimant;
}

/// <summary>
/// Minimal HS256 JWT issuer/validator built only on the BCL (HMACSHA256 + constant-time compare).
/// Chosen so the whole platform builds with zero auth NuGet packages; production would use OIDC (Entra ID / Okta)
/// with Microsoft.AspNetCore.Authentication.JwtBearer — the ClaimsPrincipal shape stays the same.
/// </summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options)
{
    private readonly JwtOptions _o = options.Value;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public (string Token, DateTimeOffset ExpiresAt) Issue(AuthUser user, DateTimeOffset now)
    {
        var exp = now.AddMinutes(_o.ExpiryMinutes);
        var header = new Dictionary<string, object> { ["alg"] = "HS256", ["typ"] = "JWT" };
        var payload = new Dictionary<string, object?>
        {
            ["iss"] = _o.Issuer,
            ["aud"] = _o.Audience,
            ["sub"] = user.Id,
            ["name"] = user.Name,
            ["email"] = user.Email,
            ["role"] = user.Role,
            ["market"] = user.Market?.ToString(),
            ["authority"] = user.AuthorityLimitUsd,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = exp.ToUnixTimeSeconds(),
        };
        var signingInput = B64(JsonSerializer.SerializeToUtf8Bytes(header, Json)) + "." +
                           B64(JsonSerializer.SerializeToUtf8Bytes(payload, Json));
        return (signingInput + "." + B64(Sign(signingInput)), exp);
    }

    public ClaimsPrincipal? Validate(string token, DateTimeOffset now)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3) return null;

            var expected = Sign(parts[0] + "." + parts[1]);
            var actual = FromB64(parts[2]);
            if (!CryptographicOperations.FixedTimeEquals(expected, actual)) return null;

            using var doc = JsonDocument.Parse(FromB64(parts[1]));
            var p = doc.RootElement;
            if (Str(p, "iss") != _o.Issuer || Str(p, "aud") != _o.Audience) return null;
            if (!p.TryGetProperty("exp", out var expEl) || expEl.GetInt64() <= now.ToUnixTimeSeconds()) return null;

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, Str(p, "sub") ?? ""),
                new(ClaimTypes.Name, Str(p, "name") ?? ""),
                new(ClaimTypes.Email, Str(p, "email") ?? ""),
                new(ClaimTypes.Role, Str(p, "role") ?? ""),
                new("authority", p.TryGetProperty("authority", out var a) ? a.GetDecimal().ToString(System.Globalization.CultureInfo.InvariantCulture) : "0"),
            };
            var market = Str(p, "market");
            if (!string.IsNullOrEmpty(market)) claims.Add(new Claim("market", market));

            return new ClaimsPrincipal(new ClaimsIdentity(claims, "ClaimFlowJwt", ClaimTypes.Name, ClaimTypes.Role));
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or KeyNotFoundException)
        {
            return null;
        }
    }

    private byte[] Sign(string input) => HMACSHA256.HashData(Encoding.UTF8.GetBytes(_o.Secret), Encoding.UTF8.GetBytes(input));

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromB64(string s)
    {
        var t = s.Replace('-', '+').Replace('_', '/');
        t = t.PadRight(t.Length + (4 - t.Length % 4) % 4, '=');
        return Convert.FromBase64String(t);
    }
}

public static class PrincipalExtensions
{
    public static CurrentUser ToCurrentUser(this ClaimsPrincipal p)
    {
        var id = p.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(id)) throw new Hosting.UnauthorizedAppException();
        _ = Enum.TryParse<Market>(p.FindFirstValue("market"), out var m);
        Market? market = p.FindFirst("market") is null ? null : m;
        var authority = decimal.TryParse(p.FindFirstValue("authority"), System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var a) ? a : 0m;
        return new CurrentUser(id, p.FindFirstValue(ClaimTypes.Name) ?? id, p.FindFirstValue(ClaimTypes.Role) ?? "", market, authority);
    }
}

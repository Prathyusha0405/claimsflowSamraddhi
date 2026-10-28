using System.Security.Cryptography;
using System.Text;
using ClaimFlow.Contracts;
using ClaimFlow.Platform.Hosting;
using ClaimFlow.Platform.Security;

var builder = WebApplication.CreateBuilder(args);
builder.AddClaimFlowDefaults("identity");
builder.Services.AddSingleton<UserDirectory>();

var app = builder.Build();
app.UseClaimFlowDefaults();

// POST /login — exchange credentials for a JWT (role, market and approval authority are baked into the token).
app.MapPost("/login", (LoginRequest req, UserDirectory users, JwtTokenService jwt, TimeProvider clock) =>
{
    var user = users.Authenticate(req.Email, req.Password)
               ?? throw new AppException(401, "INVALID_CREDENTIALS", "Invalid email or password.");
    var (token, expires) = jwt.Issue(user, clock.GetUtcNow());
    return Results.Ok(new LoginResponse(token, expires, UserDto.From(user)));
});

// Public on purpose: powers the "quick login" persona cards in the demo UI. Remove in any real deployment.
app.MapGet("/demo-users", (UserDirectory users) => Results.Ok(new
{
    password = DemoCatalog.DemoPassword,
    users = users.All.Select(UserDto.From),
}));

app.MapGet("/me", (HttpContext ctx, UserDirectory users) =>
{
    var me = ctx.User.ToCurrentUser();
    var user = users.All.FirstOrDefault(u => u.Id == me.Id) ?? throw new NotFoundException("USER_NOT_FOUND", "User no longer exists.");
    return Results.Ok(UserDto.From(user));
}).RequireAuthorization();

// Staff directory — used by managers to reassign claims.
app.MapGet("/staff", (UserDirectory users) =>
    Results.Ok(users.All.Where(u => u.Role != Roles.Claimant).Select(UserDto.From)))
    .RequireAuthorization(Policies.Staff);

app.Run();

public sealed record LoginRequest(string Email, string Password);
public sealed record LoginResponse(string Token, DateTimeOffset ExpiresAt, UserDto User);
public sealed record UserDto(string Id, string Email, string Name, string Role, Market? Market, string? Currency, decimal AuthorityLimitUsd, string Blurb)
{
    public static UserDto From(AuthUser u) => new(u.Id, u.Email, u.Name, u.Role, u.Market,
        u.Market is { } m ? Markets.CurrencyOf(m) : null, u.AuthorityLimitUsd,
        DemoCatalog.Users.FirstOrDefault(d => d.Id == u.Id)?.Blurb ?? "");
}

/// <summary>Demo user directory. Passwords are PBKDF2-hashed in memory; a real deployment would federate to an IdP.</summary>
public sealed class UserDirectory
{
    private sealed record Entry(AuthUser User, byte[] Salt, byte[] Hash);
    private readonly List<Entry> _entries = new();
    private const int Iterations = 60_000;

    public UserDirectory()
    {
        foreach (var d in DemoCatalog.Users)
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            _entries.Add(new Entry(new AuthUser(d.Id, d.Email, d.Name, d.Role, d.Market, d.AuthorityLimitUsd), salt, Hash(DemoCatalog.DemoPassword, salt)));
        }
    }

    public IReadOnlyList<AuthUser> All => _entries.Select(e => e.User).ToList();

    public AuthUser? Authenticate(string? email, string? password)
    {
        var entry = _entries.FirstOrDefault(e => string.Equals(e.User.Email, email?.Trim(), StringComparison.OrdinalIgnoreCase));
        // Always hash, even for unknown users, so timing does not reveal which emails exist.
        var salt = entry?.Salt ?? new byte[16];
        var candidate = Hash(password ?? "", salt);
        return entry is not null && CryptographicOperations.FixedTimeEquals(candidate, entry.Hash) ? entry.User : null;
    }

    private static byte[] Hash(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, 32);
}

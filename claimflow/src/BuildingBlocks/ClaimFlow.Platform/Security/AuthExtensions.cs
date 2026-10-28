using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ClaimFlow.Contracts;

namespace ClaimFlow.Platform.Security;

public static class Policies
{
    public const string Staff = "Staff";
    public const string Claimant = "Claimant";
    public const string Manager = "Manager";
}

public sealed class JwtAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    JwtTokenService jwt,
    TimeProvider clock) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ClaimFlowJwt";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());

        var principal = jwt.Validate(header["Bearer ".Length..].Trim(), clock.GetUtcNow());
        return Task.FromResult(principal is null
            ? AuthenticateResult.Fail("Invalid or expired token")
            : AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}

public static class AuthExtensions
{
    public static IServiceCollection AddClaimFlowAuth(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<JwtOptions>(config.GetSection("Jwt"));
        services.AddSingleton<JwtTokenService>();
        services.AddAuthentication(JwtAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, JwtAuthHandler>(JwtAuthHandler.SchemeName, null);
        services.AddAuthorization(o =>
        {
            o.AddPolicy(Policies.Staff, p => p.RequireRole(Roles.ClaimsOfficer, Roles.Manager));
            o.AddPolicy(Policies.Claimant, p => p.RequireRole(Roles.Claimant));
            o.AddPolicy(Policies.Manager, p => p.RequireRole(Roles.Manager));
        });
        return services;
    }
}

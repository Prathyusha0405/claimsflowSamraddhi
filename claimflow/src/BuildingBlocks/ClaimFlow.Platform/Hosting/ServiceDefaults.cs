using ClaimFlow.Platform.Messaging;
using ClaimFlow.Platform.Persistence;
using ClaimFlow.Platform.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClaimFlow.Platform.Hosting;

public sealed record ServiceInfo(string Name, DateTimeOffset StartedAt);

public static class ServiceDefaults
{
    /// <summary>
    /// Walks up from the binaries / working dir to find config/claimflow.shared.json so every service shares one
    /// place for the messaging provider, JWT secret and service URLs. Env vars still win (Messaging__Provider=Kafka).
    /// </summary>
    private static string? FindSharedConfig()
    {
        var explicitPath = Environment.GetEnvironmentVariable("CLAIMFLOW_CONFIG");
        if (!string.IsNullOrEmpty(explicitPath) && File.Exists(explicitPath)) return explicitPath;

        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(start);
            for (var i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "config", "claimflow.shared.json");
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    public static WebApplicationBuilder AddClaimFlowDefaults(this WebApplicationBuilder builder, string serviceName)
    {
        var shared = FindSharedConfig();
        if (shared is not null) builder.Configuration.AddJsonFile(shared, optional: true, reloadOnChange: false);
        builder.Configuration.AddEnvironmentVariables();   // re-add so env vars override the shared file

        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(o =>
        {
            o.SingleLine = true;
            o.TimestampFormat = "HH:mm:ss ";
            o.IncludeScopes = false;
        });
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);

        var s = builder.Services;
        s.AddSingleton(new ServiceInfo(serviceName, DateTimeOffset.UtcNow));
        s.AddSingleton(TimeProvider.System);
        s.AddHttpContextAccessor();
        s.AddTransient<CorrelationIdHandler>();
        s.Configure<JsonOptions>(o =>
        {
            foreach (var c in JsonDefaults.Options.Converters) o.SerializerOptions.Converters.Add(c);
            o.SerializerOptions.PropertyNamingPolicy = JsonDefaults.Options.PropertyNamingPolicy;
        });
        s.AddCors(o => o.AddDefaultPolicy(p => p
            .WithOrigins((builder.Configuration["Cors:Origins"] ?? "http://localhost:4200,http://127.0.0.1:4200").Split(',', StringSplitOptions.RemoveEmptyEntries))
            .AllowAnyHeader().AllowAnyMethod().WithExposedHeaders(CorrelationContext.Header)));
        s.AddClaimFlowAuth(builder.Configuration);
        return builder;
    }

    public static IServiceCollection AddJsonDataStore(this IServiceCollection services, IConfiguration config, string serviceName)
    {
        services.AddSingleton(sp =>
        {
            var dir = config["Storage:DataDir"] ?? Environment.GetEnvironmentVariable("CLAIMFLOW_DATA_DIR") ?? "data";
            var path = config.GetValue<bool>("Storage:InMemory") ? null : Path.GetFullPath(Path.Combine(dir, serviceName + ".json"));
            var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Storage");
            log.LogInformation("Data store · {Path}", path ?? "(in memory)");
            return new JsonDataStore(path, log);
        });
        return services;
    }

    public static WebApplication UseClaimFlowDefaults(this WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<ErrorHandlingMiddleware>();
        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet("/health", (ServiceInfo info, IEnumerable<IHealthContributor> contributors, TimeProvider clock) =>
        {
            var checks = contributors.ToDictionary(c => c.Name, c => c.Snapshot());
            return Results.Ok(new
            {
                service = info.Name,
                status = "Healthy",
                version = "1.0.0",
                startedAt = info.StartedAt,
                uptimeSeconds = (long)(clock.GetUtcNow() - info.StartedAt).TotalSeconds,
                checks,
            });
        }).ExcludeFromDescription();

        return app;
    }

    /// <summary>Authorisation check for service-to-service endpoints (e.g. Policy coverage check called by Claims).</summary>
    public static bool HasInternalKey(this HttpContext ctx, string expected) =>
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(ctx.Request.Headers["X-Internal-Key"].ToString()),
            System.Text.Encoding.UTF8.GetBytes(expected));
}

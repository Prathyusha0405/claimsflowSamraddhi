using System.Diagnostics;
using System.Net;
using System.Threading.RateLimiting;
using ClaimFlow.Platform.Hosting;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.AddClaimFlowDefaults("gateway");

// public route prefix → downstream service (URLs come from config/claimflow.shared.json → Services:*)
var routes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["identity"] = "Identity",
    ["policies"] = "Policy",
    ["claims"] = "Claims",
    ["notifications"] = "Notifications",
    ["reports"] = "Reporting",
};

builder.Services.AddHttpClient("proxy", c => c.Timeout = TimeSpan.FromSeconds(30))
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false })
    .AddHttpMessageHandler<CorrelationIdHandler>();
builder.Services.AddHttpClient("health", c => c.Timeout = TimeSpan.FromSeconds(2));

// Edge protection: a generous per-IP limit that still stops a runaway client.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 1500, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();
app.UseRateLimiter();
app.UseClaimFlowDefaults();

// Only these two calls are allowed without a token (needed to log in).
static bool IsPublic(string service, string path) =>
    service.Equals("identity", StringComparison.OrdinalIgnoreCase) &&
    (path.Equals("login", StringComparison.OrdinalIgnoreCase) || path.Equals("demo-users", StringComparison.OrdinalIgnoreCase));

var SkipRequestHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { "Host", "Connection", "Keep-Alive", "Transfer-Encoding", "Upgrade", "Proxy-Connection", "TE", "Trailer" };
var SkipResponseHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { "Transfer-Encoding", "Connection", "Keep-Alive", "Upgrade", "Proxy-Connection" };

// ── the reverse proxy: /api/{service}/{**path} → http://service/{**path} ────────────────────────
app.Map("/api/{service}/{**path}", async (HttpContext ctx, string service, string? path, IHttpClientFactory clients, IConfiguration cfg) =>
{
    path ??= "";
    if (!routes.TryGetValue(service, out var configKey) || cfg[$"Services:{configKey}"] is not { } baseUrl)
        throw new NotFoundException("UNKNOWN_ROUTE", $"No service is registered for '/api/{service}'.");

    if (!IsPublic(service, path) && ctx.User.Identity?.IsAuthenticated != true)
        throw new UnauthorizedAppException("Please sign in to continue.");

    // Identity exposes /login, /me …; every other service roots its routes at its own name (/claims, /policies, /notifications, /reports).
    var downstreamPath = service.Equals("identity", StringComparison.OrdinalIgnoreCase) ? path : $"{service.ToLowerInvariant()}/{path}".TrimEnd('/');
    using var request = new HttpRequestMessage(new HttpMethod(ctx.Request.Method), $"{baseUrl.TrimEnd('/')}/{downstreamPath}{ctx.Request.QueryString}");
    if (ctx.Request.ContentLength > 0 || ctx.Request.Headers.ContainsKey("Transfer-Encoding"))
        request.Content = new StreamContent(ctx.Request.Body);

    foreach (var h in ctx.Request.Headers)
    {
        if (SkipRequestHeaders.Contains(h.Key)) continue;
        if (!request.Headers.TryAddWithoutValidation(h.Key, h.Value.ToArray()))
            request.Content?.Headers.TryAddWithoutValidation(h.Key, h.Value.ToArray());
    }

    HttpResponseMessage response;
    try
    {
        response = await clients.CreateClient("proxy").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ctx.RequestAborted);
    }
    catch (HttpRequestException ex)
    {
        throw new AppException(502, "SERVICE_UNREACHABLE", $"The {service} service is not reachable right now ({ex.Message}).");
    }
    catch (TaskCanceledException) when (!ctx.RequestAborted.IsCancellationRequested)
    {
        throw new AppException(504, "SERVICE_TIMEOUT", $"The {service} service took too long to respond.");
    }

    using (response)
    {
        ctx.Response.StatusCode = (int)response.StatusCode;
        foreach (var h in response.Headers) if (!SkipResponseHeaders.Contains(h.Key)) ctx.Response.Headers[h.Key] = h.Value.ToArray();
        foreach (var h in response.Content.Headers) if (!SkipResponseHeaders.Contains(h.Key)) ctx.Response.Headers[h.Key] = h.Value.ToArray();
        await response.Content.CopyToAsync(ctx.Response.Body, ctx.RequestAborted);
    }
});

// ── system overview for the UI's "System" page: every service's health + the active messaging mode ──
app.MapGet("/api/system/health", async (IHttpClientFactory clients, IConfiguration cfg, ServiceInfo self, TimeProvider clock) =>
{
    var http = clients.CreateClient("health");
    var probes = routes.Values.Distinct().Select(async name =>
    {
        var url = cfg[$"Services:{name}"]!;
        var sw = Stopwatch.StartNew();
        try
        {
            using var res = await http.GetAsync($"{url.TrimEnd('/')}/health");
            var body = await res.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(JsonDefaults.Options);
            return new { name = name.ToLowerInvariant(), url, status = res.IsSuccessStatusCode ? "Healthy" : "Unhealthy", latencyMs = sw.ElapsedMilliseconds, details = (object?)body };
        }
        catch
        {
            return new { name = name.ToLowerInvariant(), url, status = "Down", latencyMs = sw.ElapsedMilliseconds, details = (object?)null };
        }
    });
    var services = await Task.WhenAll(probes);

    return Results.Ok(new
    {
        gateway = new { status = "Healthy", startedAt = self.StartedAt, uptimeSeconds = (long)(clock.GetUtcNow() - self.StartedAt).TotalSeconds },
        messagingProvider = cfg["Messaging:Provider"] ?? "Http",
        services,
    });
});

app.MapGet("/", () => Results.Ok(new { name = "ClaimFlow API Gateway", docs = "See README.md", health = "/api/system/health" }));

app.Run();

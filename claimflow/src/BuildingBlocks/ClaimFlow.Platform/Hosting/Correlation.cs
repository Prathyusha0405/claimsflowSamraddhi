using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace ClaimFlow.Platform.Hosting;

/// <summary>Flows a correlation id across HTTP calls, logs and events so one user action can be traced end to end.</summary>
public static class CorrelationContext
{
    public const string Header = "X-Correlation-Id";
    private static readonly AsyncLocal<string?> Id = new();

    public static string? Current
    {
        get => Id.Value;
        set => Id.Value = value;
    }
}

public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> log)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var id = ctx.Request.Headers[CorrelationContext.Header].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64) id = Guid.NewGuid().ToString("N")[..12];
        CorrelationContext.Current = id;
        ctx.Response.Headers[CorrelationContext.Header] = id;
        using (log.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
        {
            await next(ctx);
        }
    }
}

/// <summary>Adds the current correlation id to outgoing service-to-service HTTP calls.</summary>
public sealed class CorrelationIdHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var id = CorrelationContext.Current;
        if (!string.IsNullOrEmpty(id) && !request.Headers.Contains(CorrelationContext.Header))
            request.Headers.Add(CorrelationContext.Header, id);
        return base.SendAsync(request, ct);
    }
}

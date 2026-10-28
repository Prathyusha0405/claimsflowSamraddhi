using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClaimFlow.Contracts;
using ClaimFlow.Platform.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClaimFlow.Platform.Messaging;

public interface IEventHandler<in TEvent> where TEvent : IntegrationEvent
{
    Task HandleAsync(TEvent evt, CancellationToken ct);
}

public interface IEventSubscription
{
    string EventName { get; }
    Task InvokeAsync(IServiceProvider scoped, JsonElement payload, CancellationToken ct);
}

internal sealed class EventSubscription<TEvent> : IEventSubscription where TEvent : IntegrationEvent
{
    public string EventName { get; } = EventNames.Of<TEvent>();

    public async Task InvokeAsync(IServiceProvider scoped, JsonElement payload, CancellationToken ct)
    {
        var evt = payload.Deserialize<TEvent>(JsonDefaults.Options)
                  ?? throw new InvalidOperationException($"Payload for {EventName} was empty");
        foreach (var handler in scoped.GetServices<IEventHandler<TEvent>>())
            await handler.HandleAsync(evt, ct);
    }
}

/// <summary>
/// Consumer-side entry point shared by both transports: the HTTP endpoint and the Kafka consumer both call this.
/// Unknown event types are ignored; duplicates (at-least-once delivery) are skipped; handlers are idempotent anyway.
/// </summary>
public sealed class EventDispatcher(
    IServiceProvider root,
    IEnumerable<IEventSubscription> subscriptions,
    MessagingStats stats,
    ILogger<EventDispatcher> log)
{
    private readonly Dictionary<string, IEventSubscription> _byName = subscriptions.ToDictionary(s => s.EventName);
    private readonly ConcurrentDictionary<string, byte> _seen = new();
    private readonly ConcurrentQueue<string> _order = new();
    private const int RememberLast = 5000;

    public async Task DispatchAsync(EventEnvelope envelope, CancellationToken ct)
    {
        if (!_byName.TryGetValue(envelope.Type, out var sub))
        {
            stats.MarkIgnored();
            return;
        }

        if (_seen.ContainsKey(envelope.Id))
        {
            log.LogDebug("Duplicate event {EventId} ({Type}) skipped", envelope.Id, envelope.Type);
            return;
        }

        CorrelationContext.Current = envelope.CorrelationId;
        using var scope = root.CreateScope();
        await sub.InvokeAsync(scope.ServiceProvider, envelope.Payload, ct);

        if (_seen.TryAdd(envelope.Id, 0))
        {
            _order.Enqueue(envelope.Id);
            while (_order.Count > RememberLast && _order.TryDequeue(out var old)) _seen.TryRemove(old, out _);
        }
        stats.MarkConsumed();
        log.LogInformation("◀ {Type} {Key} handled", envelope.Type, envelope.Key);
    }
}

public static class DispatchingExtensions
{
    /// <summary>Registers <typeparamref name="THandler"/> for <typeparamref name="TEvent"/> and subscribes the service to that event type.</summary>
    public static IServiceCollection AddEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : IntegrationEvent
        where THandler : class, IEventHandler<TEvent>
    {
        services.AddScoped<IEventHandler<TEvent>, THandler>();
        var already = services.Any(d => d.ServiceType == typeof(IEventSubscription)
                                        && d.ImplementationInstance is EventSubscription<TEvent>);
        if (!already) services.AddSingleton<IEventSubscription>(new EventSubscription<TEvent>());
        return services;
    }

    /// <summary>HTTP subscriber endpoint used when Messaging:Provider = Http. Not exposed through the gateway.</summary>
    public static IEndpointRouteBuilder MapEventSubscriber(this IEndpointRouteBuilder app)
    {
        app.MapPost("/internal/events", async (HttpContext ctx, EventDispatcher dispatcher, IOptions<MessagingOptions> options) =>
        {
            var key = ctx.Request.Headers["X-Internal-Key"].ToString();
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(options.Value.InternalKey)))
                return Results.Unauthorized();

            var envelope = await ctx.Request.ReadFromJsonAsync<EventEnvelope>(JsonDefaults.Options, ctx.RequestAborted);
            if (envelope is null) return Results.BadRequest();

            await dispatcher.DispatchAsync(envelope, ctx.RequestAborted);
            return Results.Accepted();
        }).ExcludeFromDescription();
        return app;
    }
}

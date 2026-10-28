using System.Reflection;
using System.Text.Json;
using ClaimFlow.Contracts;
using ClaimFlow.Platform.Persistence;

namespace ClaimFlow.Platform.Messaging;

/// <summary>The wire format for every event, identical on HTTP and Kafka.</summary>
public sealed record EventEnvelope(
    string Id,
    string Type,
    string Key,
    DateTimeOffset OccurredAt,
    string? CorrelationId,
    string Producer,
    JsonElement Payload);

/// <summary>A row of the transactional outbox: written in the same transaction as the business change.</summary>
public sealed class OutboxMessage : IDocument
{
    /// <summary>Zero-padded sequence number — lexical order == publish order.</summary>
    public string Id { get; set; } = "";
    public string EventType { get; set; } = "";
    public string Key { get; set; } = "";
    public string EnvelopeJson { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    /// <summary>Targets (subscriber URL or "kafka") that already acknowledged this message.</summary>
    public List<string> DeliveredTo { get; set; } = new();
}

public static class EventNames
{
    public static string Of(Type eventType) =>
        eventType.GetCustomAttribute<EventNameAttribute>()?.Name
        ?? throw new InvalidOperationException($"{eventType.Name} is missing [EventName]");

    public static string Of<T>() where T : IntegrationEvent => Of(typeof(T));
}

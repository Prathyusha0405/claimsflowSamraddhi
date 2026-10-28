using ClaimFlow.Platform.Hosting;
using ClaimFlow.Platform.Persistence;
using Microsoft.Extensions.Options;

namespace ClaimFlow.Platform.Messaging;

/// <summary>Thread-safe counters surfaced on /health and the UI's System page.</summary>
public sealed class MessagingStats
{
    private long _published, _consumed, _ignored, _failed;
    private long _lastPublishedTicks, _lastConsumedTicks;
    private string? _lastError;

    public long Published => Interlocked.Read(ref _published);
    public long Consumed => Interlocked.Read(ref _consumed);
    public long Ignored => Interlocked.Read(ref _ignored);
    public long Failed => Interlocked.Read(ref _failed);
    public string? LastError => Volatile.Read(ref _lastError);
    public DateTimeOffset? LastPublishedAt => ToTime(Interlocked.Read(ref _lastPublishedTicks));
    public DateTimeOffset? LastConsumedAt => ToTime(Interlocked.Read(ref _lastConsumedTicks));

    public void MarkPublished() { Interlocked.Increment(ref _published); Interlocked.Exchange(ref _lastPublishedTicks, DateTime.UtcNow.Ticks); Volatile.Write(ref _lastError, null); }
    public void MarkConsumed() { Interlocked.Increment(ref _consumed); Interlocked.Exchange(ref _lastConsumedTicks, DateTime.UtcNow.Ticks); }
    public void MarkIgnored() => Interlocked.Increment(ref _ignored);
    public void MarkFailed(string error) { Interlocked.Increment(ref _failed); Volatile.Write(ref _lastError, error); }

    private static DateTimeOffset? ToTime(long ticks) => ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
}

public interface IHealthContributor
{
    string Name { get; }
    object Snapshot();
}

public sealed class MessagingHealth(
    IOptions<MessagingOptions> options,
    MessagingStats stats,
    IServiceProvider sp) : IHealthContributor
{
    public string Name => "messaging";

    public object Snapshot()
    {
        var outbox = sp.GetService(typeof(DocumentCollection<OutboxMessage>)) as DocumentCollection<OutboxMessage>;
        return new
        {
            provider = options.Value.IsKafka ? "Kafka" : "Http",
            transport = options.Value.IsKafka ? options.Value.Kafka.BootstrapServers : "direct HTTP fan-out",
            outboxPending = outbox?.Count() ?? 0,
            published = stats.Published,
            consumed = stats.Consumed,
            ignored = stats.Ignored,
            failed = stats.Failed,
            lastPublishedAt = stats.LastPublishedAt,
            lastConsumedAt = stats.LastConsumedAt,
            lastError = stats.LastError,
        };
    }
}

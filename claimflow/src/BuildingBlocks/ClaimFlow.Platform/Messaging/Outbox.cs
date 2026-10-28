using System.Text.Json;
using ClaimFlow.Contracts;
using ClaimFlow.Platform.Hosting;
using ClaimFlow.Platform.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ClaimFlow.Platform.Messaging;

public interface IMessageTransport
{
    string Name { get; }

    /// <summary>
    /// Independent delivery targets. HTTP mode: one per subscriber service. Kafka mode: a single "kafka" target.
    /// A message leaves the outbox once every target acknowledged it.
    /// </summary>
    IReadOnlyList<string> Targets { get; }

    Task DeliverAsync(OutboxMessage message, string target, CancellationToken ct);
}

public sealed class OutboxSignal
{
    private readonly SemaphoreSlim _semaphore = new(0, int.MaxValue);

    public void Release()
    {
        try { _semaphore.Release(); } catch (SemaphoreFullException) { }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => _semaphore.WaitAsync(timeout, ct);
}

/// <summary>
/// Transactional outbox: <see cref="Enqueue"/> must run inside the same store transaction as the state change,
/// so "claim saved" and "event recorded" commit atomically — no dual-write problem, no lost events.
/// </summary>
public sealed class OutboxWriter(
    JsonDataStore store,
    DocumentCollection<OutboxMessage> outbox,
    OutboxSignal signal,
    ServiceInfo info)
{
    public void Enqueue(IntegrationEvent evt, string key)
    {
        var type = evt.GetType();
        var name = EventNames.Of(type);
        var payload = JsonSerializer.SerializeToElement(evt, type, JsonDefaults.Options);
        var envelope = new EventEnvelope(evt.EventId, name, key, evt.OccurredAt, CorrelationContext.Current, info.Name, payload);
        var seq = store.NextSequence("outbox");
        outbox.Upsert(new OutboxMessage
        {
            Id = seq.ToString("D12"),
            EventType = name,
            Key = key,
            EnvelopeJson = JsonSerializer.Serialize(envelope, JsonDefaults.Options),
            CreatedAt = DateTimeOffset.UtcNow,
        });
        store.AfterCommit(signal.Release);
    }
}

/// <summary>
/// Relays outbox rows to the configured transport, in order, with per-target exponential back-off.
/// If one subscriber is down, delivery to it pauses (preserving its order) while others keep flowing.
/// </summary>
public sealed class OutboxDispatcher(
    JsonDataStore store,
    DocumentCollection<OutboxMessage> outbox,
    IMessageTransport transport,
    OutboxSignal signal,
    MessagingStats stats,
    ILogger<OutboxDispatcher> log) : BackgroundService
{
    private sealed class TargetState
    {
        public int Failures;
        public DateTimeOffset NextAttemptAt = DateTimeOffset.MinValue;
    }

    private readonly Dictionary<string, TargetState> _targets = new();

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Yield();
        log.LogInformation("Outbox dispatcher started · transport={Transport} · targets=[{Targets}]",
            transport.Name, string.Join(", ", transport.Targets));

        while (!ct.IsCancellationRequested)
        {
            try { await PumpAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Outbox pump failed"); }

            try { await signal.WaitAsync(TimeSpan.FromMilliseconds(750), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task PumpAsync(CancellationToken ct)
    {
        var pending = outbox.Query().Take(200).ToList();
        if (pending.Count == 0) return;

        var blocked = new HashSet<string>();
        foreach (var msg in pending)
        {
            foreach (var target in transport.Targets)
            {
                if (msg.DeliveredTo.Contains(target) || blocked.Contains(target)) continue;

                var state = StateOf(target);
                if (state.NextAttemptAt > DateTimeOffset.UtcNow) { blocked.Add(target); continue; }

                try
                {
                    await transport.DeliverAsync(msg, target, ct);
                    state.Failures = 0;
                    msg.DeliveredTo.Add(target);
                    stats.MarkPublished();
                    Persist(msg);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    state.Failures++;
                    var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, state.Failures - 1)));
                    state.NextAttemptAt = DateTimeOffset.UtcNow + delay;
                    blocked.Add(target);
                    msg.Attempts++;
                    msg.LastError = ex.Message;
                    stats.MarkFailed($"{target}: {ex.Message}");
                    log.LogWarning("Delivery of {Type} to {Target} failed ({Error}). Retrying in {Delay}s — {Pending} message(s) waiting",
                        msg.EventType, target, ex.Message, delay.TotalSeconds, pending.Count);
                    TryPersist(msg);
                }
            }
        }
    }

    private TargetState StateOf(string target)
    {
        if (!_targets.TryGetValue(target, out var s)) _targets[target] = s = new TargetState();
        return s;
    }

    private void Persist(OutboxMessage msg) => store.Transaction(() =>
    {
        if (transport.Targets.All(t => msg.DeliveredTo.Contains(t))) outbox.Delete(msg.Id);
        else outbox.Upsert(msg);
    });

    private void TryPersist(OutboxMessage msg)
    {
        try { store.Transaction(() => { if (outbox.Exists(msg.Id)) outbox.Upsert(msg); }); }
        catch (Exception ex) { log.LogDebug(ex, "Could not persist outbox attempt info"); }
    }
}

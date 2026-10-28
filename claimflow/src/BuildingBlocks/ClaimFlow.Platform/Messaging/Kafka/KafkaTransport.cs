using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ClaimFlow.Platform.Hosting;

namespace ClaimFlow.Platform.Messaging.Kafka;

/// <summary>
/// Kafka mode producer side. Keyed by claimId so all events of one claim land on the same partition (ordered).
/// Idempotent producer + acks=all gives exactly-once-per-attempt on the broker; the outbox gives at-least-once end to end.
/// </summary>
public sealed class KafkaTransport(
    IOptions<MessagingOptions> options,
    ServiceInfo info,
    ILogger<KafkaTransport> log) : IMessageTransport, IDisposable
{
    private readonly KafkaMessagingOptions _o = options.Value.Kafka;
    private IProducer<string, string>? _producer;
    private bool _topicsEnsured;
    private readonly SemaphoreSlim _init = new(1, 1);

    public string Name => "kafka";
    public IReadOnlyList<string> Targets { get; } = ["kafka"];

    public async Task DeliverAsync(OutboxMessage message, string target, CancellationToken ct)
    {
        var producer = await GetProducerAsync(ct);
        var msg = new Message<string, string>
        {
            Key = message.Key,
            Value = message.EnvelopeJson,
            Headers = new Headers
            {
                { "event-type", Encoding.UTF8.GetBytes(message.EventType) },
                { "producer", Encoding.UTF8.GetBytes(info.Name) },
            },
        };

        try
        {
            var result = await producer.ProduceAsync(_o.Topic, msg, ct);
            log.LogInformation("▶ {Type} {Key} → {Topic}[{Partition}]@{Offset}",
                message.EventType, message.Key, result.Topic, result.Partition.Value, result.Offset.Value);
        }
        catch (ProduceException<string, string> ex)
        {
            throw new InvalidOperationException($"Kafka produce failed: {ex.Error.Reason}", ex);
        }
    }

    private async Task<IProducer<string, string>> GetProducerAsync(CancellationToken ct)
    {
        await _init.WaitAsync(ct);
        try
        {
            if (!_topicsEnsured)
            {
                await KafkaTopics.EnsureAsync(_o, log, ct);
                _topicsEnsured = true;
            }

            return _producer ??= new ProducerBuilder<string, string>(new ProducerConfig
            {
                BootstrapServers = _o.BootstrapServers,
                ClientId = $"{info.Name}-producer",
                Acks = Acks.All,
                EnableIdempotence = true,
                MessageTimeoutMs = _o.MessageTimeoutMs,
            }).SetErrorHandler((_, e) => log.LogDebug("Kafka producer: {Reason}", e.Reason)).Build();
        }
        finally
        {
            _init.Release();
        }
    }

    public void Dispose()
    {
        try { _producer?.Flush(TimeSpan.FromSeconds(2)); } catch { /* shutting down */ }
        _producer?.Dispose();
    }
}

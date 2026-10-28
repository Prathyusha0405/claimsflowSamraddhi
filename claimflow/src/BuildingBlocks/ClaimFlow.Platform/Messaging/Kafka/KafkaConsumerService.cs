using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ClaimFlow.Platform.Hosting;

namespace ClaimFlow.Platform.Messaging.Kafka;

/// <summary>
/// Kafka mode consumer side. One consumer group per service (group id = service name) so every subscriber gets every event.
/// Offsets are committed only after the handler succeeded (at-least-once). Poison messages go to the dead-letter topic.
/// The service starts fine without a broker and keeps retrying in the background.
/// </summary>
public sealed class KafkaConsumerService(
    IOptions<MessagingOptions> options,
    EventDispatcher dispatcher,
    ServiceInfo info,
    MessagingStats stats,
    ILogger<KafkaConsumerService> log) : BackgroundService
{
    private IProducer<string, string>? _dlqProducer;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Yield();
        var o = options.Value.Kafka;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await KafkaTopics.EnsureAsync(o, log, ct);
                await ConsumeLoopAsync(o, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                stats.MarkFailed(ex.Message);
                log.LogWarning("Kafka not reachable at {Servers} ({Message}). Retrying in 5s — the service keeps working, events queue in the outbox.",
                    o.BootstrapServers, ex.Message);
                try { await Task.Delay(TimeSpan.FromSeconds(5), ct); } catch (OperationCanceledException) { break; }
            }
        }

        _dlqProducer?.Dispose();
    }

    private async Task ConsumeLoopAsync(KafkaMessagingOptions o, CancellationToken ct)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = o.BootstrapServers,
            GroupId = info.Name,
            ClientId = $"{info.Name}-consumer",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            SessionTimeoutMs = 15000,
        };

        using var consumer = new ConsumerBuilder<string, string>(config)
            .SetErrorHandler((_, e) => log.LogDebug("Kafka consumer: {Reason}", e.Reason))
            .Build();
        consumer.Subscribe(o.Topic);
        log.LogInformation("Kafka consumer joined group '{Group}' on topic '{Topic}' ({Servers})", info.Name, o.Topic, o.BootstrapServers);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result;
                try
                {
                    result = consumer.Consume(TimeSpan.FromMilliseconds(500));
                }
                catch (ConsumeException ex)
                {
                    if (ex.Error.IsFatal) throw;
                    log.LogDebug("Consume warning: {Reason}", ex.Error.Reason);
                    await Task.Delay(1000, ct);
                    continue;
                }

                if (result?.Message is null) continue;

                await HandleAsync(result, o, ct);
                consumer.Commit(result);
            }
        }
        finally
        {
            try { consumer.Close(); } catch { /* best effort */ }
        }
    }

    private async Task HandleAsync(ConsumeResult<string, string> result, KafkaMessagingOptions o, CancellationToken ct)
    {
        EventEnvelope? envelope = null;
        Exception? last = null;

        for (var attempt = 1; attempt <= Math.Max(1, o.MaxHandlerAttempts); attempt++)
        {
            try
            {
                envelope ??= JsonSerializer.Deserialize<EventEnvelope>(result.Message.Value, JsonDefaults.Options);
                if (envelope is null) throw new JsonException("Envelope was null");
                await dispatcher.DispatchAsync(envelope, ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (JsonException ex) { last = ex; break; }   // malformed → retrying will not help
            catch (Exception ex)
            {
                last = ex;
                log.LogWarning("Handler failed for {Key} (attempt {Attempt}/{Max}): {Error}", result.Message.Key, attempt, o.MaxHandlerAttempts, ex.Message);
                await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt), ct);
            }
        }

        await DeadLetterAsync(result, o, last, ct);
    }

    private async Task DeadLetterAsync(ConsumeResult<string, string> result, KafkaMessagingOptions o, Exception? error, CancellationToken ct)
    {
        stats.MarkFailed($"dead-lettered {result.Message.Key}: {error?.Message}");
        log.LogError("Dead-lettering message {Key} from {Topic}[{Partition}]@{Offset}: {Error}",
            result.Message.Key, result.Topic, result.Partition.Value, result.Offset.Value, error?.Message);

        _dlqProducer ??= new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = o.BootstrapServers,
            ClientId = $"{info.Name}-dlq",
            Acks = Acks.All,
            MessageTimeoutMs = o.MessageTimeoutMs,
        }).Build();

        await _dlqProducer.ProduceAsync(o.DeadLetterTopic, new Message<string, string>
        {
            Key = result.Message.Key,
            Value = result.Message.Value,
            Headers = new Headers
            {
                { "error", Encoding.UTF8.GetBytes(error?.Message ?? "unknown") },
                { "source-topic", Encoding.UTF8.GetBytes(result.Topic) },
                { "source-partition", Encoding.UTF8.GetBytes(result.Partition.Value.ToString()) },
                { "source-offset", Encoding.UTF8.GetBytes(result.Offset.Value.ToString()) },
                { "failed-service", Encoding.UTF8.GetBytes(info.Name) },
            },
        }, ct);
    }
}

using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Logging;

namespace ClaimFlow.Platform.Messaging.Kafka;

public static class KafkaTopics
{
    /// <summary>Creates the main + dead-letter topics if they do not exist yet. Safe to call repeatedly.</summary>
    public static async Task EnsureAsync(KafkaMessagingOptions o, ILogger log, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = o.BootstrapServers }).Build();
        var specs = new[] { o.Topic, o.DeadLetterTopic }
            .Select(t => new TopicSpecification { Name = t, NumPartitions = o.Partitions, ReplicationFactor = (short)o.ReplicationFactor })
            .ToList();
        try
        {
            await admin.CreateTopicsAsync(specs, new CreateTopicsOptions { RequestTimeout = TimeSpan.FromSeconds(10) });
            log.LogInformation("Kafka topics created: {Topics}", string.Join(", ", specs.Select(s => s.Name)));
        }
        catch (CreateTopicsException ex)
        {
            var real = ex.Results.Where(r => r.Error.IsError && r.Error.Code != ErrorCode.TopicAlreadyExists).ToList();
            if (real.Count > 0) throw;
        }
    }
}

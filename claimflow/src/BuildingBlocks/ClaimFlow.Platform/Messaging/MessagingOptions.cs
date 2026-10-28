using ClaimFlow.Contracts;

namespace ClaimFlow.Platform.Messaging;

public sealed class MessagingOptions
{
    /// <summary>"Http" (default — no broker needed) or "Kafka".</summary>
    public string Provider { get; set; } = "Http";

    /// <summary>Shared secret for service-to-service calls (event delivery, coverage checks).</summary>
    public string InternalKey { get; set; } = "claimflow-internal-dev-key";

    public HttpMessagingOptions Http { get; set; } = new();
    public KafkaMessagingOptions Kafka { get; set; } = new();

    public bool IsKafka => string.Equals(Provider, "Kafka", StringComparison.OrdinalIgnoreCase);
}

public sealed class HttpMessagingOptions
{
    /// <summary>Base URLs of services that subscribe to events when running without Kafka.</summary>
    public string[] Subscribers { get; set; } = [];
}

public sealed class KafkaMessagingOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string Topic { get; set; } = Topics.ClaimEvents;
    public string DeadLetterTopic { get; set; } = Topics.ClaimEventsDlq;
    public int Partitions { get; set; } = 3;
    public int ReplicationFactor { get; set; } = 1;
    public int MessageTimeoutMs { get; set; } = 8000;
    public int MaxHandlerAttempts { get; set; } = 3;
}

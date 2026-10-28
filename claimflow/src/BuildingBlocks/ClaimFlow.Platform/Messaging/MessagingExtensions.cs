using ClaimFlow.Platform.Messaging.Kafka;
using ClaimFlow.Platform.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClaimFlow.Platform.Messaging;

public static class MessagingExtensions
{
    private static bool IsKafka(IConfiguration config) =>
        string.Equals(config["Messaging:Provider"], "Kafka", StringComparison.OrdinalIgnoreCase);

    private static void AddCommon(IServiceCollection services, IConfiguration config)
    {
        services.Configure<MessagingOptions>(config.GetSection("Messaging"));
        services.TryAddSingleton<MessagingStats>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, MessagingHealth>());
    }

    /// <summary>
    /// For services that PUBLISH events (Claims). Requires a <see cref="JsonDataStore"/> in DI: the outbox lives in the
    /// same store as the business data so they commit atomically. The transport is chosen by Messaging:Provider.
    /// </summary>
    public static IServiceCollection AddEventPublishing(this IServiceCollection services, IConfiguration config)
    {
        AddCommon(services, config);
        services.AddSingleton<OutboxSignal>();
        services.AddSingleton(sp => sp.GetRequiredService<JsonDataStore>().Collection<OutboxMessage>("outbox"));
        services.AddSingleton<OutboxWriter>();

        if (IsKafka(config))
        {
            services.AddSingleton<IMessageTransport, KafkaTransport>();
        }
        else
        {
            services.AddHttpClient(HttpFanoutTransport.ClientName, c => c.Timeout = TimeSpan.FromSeconds(5));
            services.AddSingleton<IMessageTransport, HttpFanoutTransport>();
        }

        services.AddHostedService<OutboxDispatcher>();
        return services;
    }

    /// <summary>
    /// For services that SUBSCRIBE (Notifications, Reporting). Register handlers with AddEventHandler&lt;,&gt;().
    /// HTTP mode: events arrive on POST /internal/events (call MapEventSubscriber). Kafka mode: a consumer group is joined.
    /// </summary>
    public static IServiceCollection AddEventSubscribing(this IServiceCollection services, IConfiguration config)
    {
        AddCommon(services, config);
        services.AddSingleton<EventDispatcher>();
        if (IsKafka(config)) services.AddHostedService<KafkaConsumerService>();
        return services;
    }
}

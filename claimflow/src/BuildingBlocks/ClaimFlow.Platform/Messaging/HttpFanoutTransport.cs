using System.Text;
using Microsoft.Extensions.Options;

namespace ClaimFlow.Platform.Messaging;

/// <summary>
/// "No Kafka" mode: the outbox POSTs each envelope to every subscriber's /internal/events endpoint.
/// Same envelope, same at-least-once + ordered-per-target semantics as the Kafka path — only the wire differs.
/// </summary>
public sealed class HttpFanoutTransport(IHttpClientFactory factory, IOptions<MessagingOptions> options) : IMessageTransport
{
    public const string ClientName = "claimflow-events";

    public string Name => "http-fanout";
    public IReadOnlyList<string> Targets => options.Value.Http.Subscribers;

    public async Task DeliverAsync(OutboxMessage message, string target, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, target.TrimEnd('/') + "/internal/events")
        {
            Content = new StringContent(message.EnvelopeJson, Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("X-Internal-Key", options.Value.InternalKey);
        using var res = await factory.CreateClient(ClientName).SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
            throw new HttpRequestException($"subscriber answered HTTP {(int)res.StatusCode}");
    }
}

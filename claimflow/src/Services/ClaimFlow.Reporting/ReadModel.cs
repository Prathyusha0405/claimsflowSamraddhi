using ClaimFlow.Contracts;
using ClaimFlow.Platform.Messaging;
using ClaimFlow.Platform.Persistence;

namespace ClaimFlow.Reporting;

/// <summary>The latest known state of a claim, built purely from events (CQRS read model).</summary>
public sealed class ClaimView : IDocument
{
    public string Id { get; set; } = "";
    public ClaimSnapshot Snapshot { get; set; } = null!;
    public DateTimeOffset LastEventAt { get; set; }
}

/// <summary>Rolling feed of what this service has processed — powers the live "event flow" panel in the UI.</summary>
public sealed class RecentEvent : IDocument
{
    public string Id { get; set; } = "";
    public string EventType { get; set; } = "";
    public string ClaimNumber { get; set; } = "";
    public string Summary { get; set; } = "";
    public Market Market { get; set; }
    public ClaimStatus Status { get; set; }
    public int Version { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}

/// <summary>
/// One generic projector registered for every claim event type. Idempotent and order-tolerant:
/// a snapshot is applied only if its Version is newer than what we hold, so duplicates (at-least-once delivery)
/// and stale / reordered events (HTTP retries, Kafka rebalances) are harmless.
/// </summary>
public sealed class ProjectionHandler<TEvent>(
    DocumentCollection<ClaimView> views,
    DocumentCollection<RecentEvent> recent,
    JsonDataStore store,
    TimeProvider clock) : IEventHandler<TEvent> where TEvent : ClaimEvent
{
    private const int FeedSize = 150;

    public Task HandleAsync(TEvent evt, CancellationToken ct)
    {
        store.Transaction(() =>
        {
            var existing = views.Get(evt.Claim.ClaimId);
            var applied = existing is null || evt.Claim.Version > existing.Snapshot.Version;
            if (applied)
                views.Upsert(new ClaimView { Id = evt.Claim.ClaimId, Snapshot = evt.Claim, LastEventAt = evt.OccurredAt });

            var seq = store.NextSequence("recent");
            recent.Upsert(new RecentEvent
            {
                Id = seq.ToString("D10"),
                EventType = EventNames.Of(typeof(TEvent)),
                ClaimNumber = evt.Claim.ClaimNumber,
                Summary = applied ? Describe(evt) : Describe(evt) + " (stale — ignored)",
                Market = evt.Claim.Market,
                Status = evt.Claim.Status,
                Version = evt.Claim.Version,
                OccurredAt = evt.OccurredAt,
                ReceivedAt = clock.GetUtcNow(),
            });
            var overflow = recent.Count() - FeedSize;
            if (overflow > 0)
                foreach (var old in recent.Query().Take(overflow)) recent.Delete(old.Id);
        });
        return Task.CompletedTask;
    }

    private static string Describe(TEvent e) => e switch
    {
        ClaimAssigned a => $"assigned to {a.OfficerName}",
        InformationRequested => "information requested",
        InformationProvided => "claimant replied",
        ClaimAssessed a => $"assessed · payable {e.Claim.Currency} {a.PayableAmount:N0}",
        ClaimApproved a => $"approved · {e.Claim.Currency} {a.ApprovedAmount:N0}",
        ClaimRejected => "declined",
        ClaimSettled s => $"paid · {e.Claim.Currency} {s.PaidAmount:N0}",
        ClaimWithdrawn => "withdrawn",
        _ => "submitted",
    };
}

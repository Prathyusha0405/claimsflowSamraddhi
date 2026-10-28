using ClaimFlow.Contracts;
using ClaimFlow.Platform.Messaging;
using ClaimFlow.Platform.Persistence;

namespace ClaimFlow.Notifications;

public sealed class NotificationDoc : IDocument
{
    public string Id { get; set; } = "";
    /// <summary>A user id, or "role:Manager" for a role-wide notification.</summary>
    public string RecipientId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    /// <summary>info | action | success | warning | danger — drives the colour/icon in the UI.</summary>
    public string Kind { get; set; } = "info";
    public string ClaimId { get; set; } = "";
    public string ClaimNumber { get; set; } = "";
    public string EventType { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}

public sealed record NotificationDto(string Id, string Title, string Body, string Kind, string ClaimId, string ClaimNumber, DateTimeOffset CreatedAt, bool Read)
{
    public static NotificationDto From(NotificationDoc n) => new(n.Id, n.Title, n.Body, n.Kind, n.ClaimId, n.ClaimNumber, n.CreatedAt, n.ReadAt is not null);
}

/// <summary>
/// Turns claim events into in-app notifications (and a simulated e-mail log line).
/// Idempotent: the notification id is derived from (event id, recipient), so redelivery changes nothing.
/// Replays of old events (e.g. a fresh Kafka consumer group reading from the start) are stored as already-read and not e-mailed.
/// </summary>
public sealed class ClaimNotificationHandler(
    DocumentCollection<NotificationDoc> notifications,
    JsonDataStore store,
    TimeProvider clock,
    ILogger<ClaimNotificationHandler> log) :
    IEventHandler<ClaimSubmitted>, IEventHandler<ClaimAssigned>, IEventHandler<InformationRequested>,
    IEventHandler<InformationProvided>, IEventHandler<ClaimAssessed>, IEventHandler<ClaimApproved>,
    IEventHandler<ClaimRejected>, IEventHandler<ClaimSettled>, IEventHandler<ClaimWithdrawn>
{
    private static string Money(ClaimSnapshot c, decimal amount) => $"{c.Currency} {amount:N2}";

    public Task HandleAsync(ClaimSubmitted e, CancellationToken ct) =>
        Notify(e, e.Claim.ClaimantId, "We've received your claim",
            $"Claim {e.Claim.ClaimNumber} is registered. A claims officer will pick it up shortly.", "success");

    public Task HandleAsync(ClaimAssigned e, CancellationToken ct)
    {
        Notify(e, e.Claim.ClaimantId, "Your claim is being reviewed", $"{e.OfficerName} is now handling claim {e.Claim.ClaimNumber}.", "info");
        // Only tell the officer when somebody else assigned it to them (self pick-up needs no ping).
        return e.AssignedBy is not null && e.AssignedBy != e.OfficerName
            ? Notify(e, e.OfficerId, "New claim assigned to you", $"{e.AssignedBy} assigned {e.Claim.ClaimNumber} ({e.Claim.ClaimantName}) to you.", "action")
            : Task.CompletedTask;
    }

    public Task HandleAsync(InformationRequested e, CancellationToken ct) =>
        Notify(e, e.Claim.ClaimantId, "Action needed: more information required",
            $"{e.RequestedBy} asked: “{e.Question}”", "action");

    public Task HandleAsync(InformationProvided e, CancellationToken ct) =>
        e.Claim.AssignedOfficerId is { } officer
            ? Notify(e, officer, "Claimant responded", $"{e.Claim.ClaimantName} answered the question on {e.Claim.ClaimNumber}.", "info")
            : Task.CompletedTask;

    public Task HandleAsync(ClaimAssessed e, CancellationToken ct) =>
        e.RequiresManagerApproval
            ? Notify(e, "role:Manager", "Approval needed",
                $"{e.AssessedBy} assessed {e.Claim.ClaimNumber} at {Money(e.Claim, e.PayableAmount)} — above their authority.", "warning")
            : Task.CompletedTask;

    public Task HandleAsync(ClaimApproved e, CancellationToken ct) =>
        Notify(e, e.Claim.ClaimantId, "Good news — your claim is approved",
            $"Claim {e.Claim.ClaimNumber} was approved for {Money(e.Claim, e.ApprovedAmount)}. Payment will follow.", "success");

    public Task HandleAsync(ClaimRejected e, CancellationToken ct) =>
        Notify(e, e.Claim.ClaimantId, "Decision on your claim",
            $"We're sorry — claim {e.Claim.ClaimNumber} could not be accepted. Reason: {e.Reason}", "danger");

    public Task HandleAsync(ClaimSettled e, CancellationToken ct) =>
        Notify(e, e.Claim.ClaimantId, "Payment sent",
            $"{Money(e.Claim, e.PaidAmount)} was paid for claim {e.Claim.ClaimNumber} (ref {e.PaymentReference}).", "success");

    public Task HandleAsync(ClaimWithdrawn e, CancellationToken ct) =>
        e.Claim.AssignedOfficerId is { } officer
            ? Notify(e, officer, "Claim withdrawn", $"{e.Claim.ClaimantName} withdrew {e.Claim.ClaimNumber}.", "info")
            : Task.CompletedTask;

    private Task Notify(ClaimEvent e, string recipient, string title, string body, string kind)
    {
        var id = $"{e.EventId}:{recipient}";
        var now = clock.GetUtcNow();
        var stale = now - e.OccurredAt > TimeSpan.FromHours(24);

        store.Transaction(() =>
        {
            if (notifications.Exists(id)) return;   // already processed (at-least-once delivery)
            notifications.Upsert(new NotificationDoc
            {
                Id = id, RecipientId = recipient, Title = title, Body = body, Kind = kind,
                ClaimId = e.Claim.ClaimId, ClaimNumber = e.Claim.ClaimNumber, EventType = EventNames.Of(e.GetType()),
                CreatedAt = e.OccurredAt, ReadAt = stale ? e.OccurredAt : null,
            });
        });

        if (!stale)
        {
            var email = DemoCatalog.Users.FirstOrDefault(u => u.Id == recipient)?.Email ?? recipient;
            log.LogInformation("📧 (simulated e-mail) to {To}: {Subject}", email, title);
        }
        return Task.CompletedTask;
    }
}

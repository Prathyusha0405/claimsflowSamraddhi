namespace ClaimFlow.Contracts;

/// <summary>Names the wire-level type of an integration event, e.g. "claim.submitted".</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EventNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>
/// Denormalised state of a claim after a change ("event-carried state transfer").
/// Consumers (Reporting, Notifications) can build their views without calling the Claims service back.
/// <see cref="Version"/> increases on every change so consumers can ignore duplicates and stale / out-of-order events.
/// </summary>
public sealed record ClaimSnapshot(
    string ClaimId,
    string ClaimNumber,
    int Version,
    string PolicyNumber,
    string ClaimantId,
    string ClaimantName,
    Market Market,
    ClaimType Type,
    ClaimStatus Status,
    ClaimPriority Priority,
    string Currency,
    decimal ClaimedAmount,
    decimal Reserve,
    decimal? ApprovedAmount,
    decimal PaidAmount,
    string? AssignedOfficerId,
    string? AssignedOfficerName,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? FirstAssignedAt,
    DateTimeOffset? AwaitingInfoSince,
    DateTimeOffset? DecidedAt,
    DateTimeOffset? SettledAt,
    DateTimeOffset UpdatedAt);

public abstract record IntegrationEvent
{
    public string EventId { get; init; } = Guid.NewGuid().ToString();
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public abstract record ClaimEvent : IntegrationEvent
{
    public required ClaimSnapshot Claim { get; init; }
}

[EventName("claim.submitted")]
public sealed record ClaimSubmitted : ClaimEvent;

[EventName("claim.assigned")]
public sealed record ClaimAssigned : ClaimEvent
{
    public required string OfficerId { get; init; }
    public required string OfficerName { get; init; }
    public string? PreviousOfficerId { get; init; }
    public string? AssignedBy { get; init; }
}

[EventName("claim.information-requested")]
public sealed record InformationRequested : ClaimEvent
{
    public required string RequestId { get; init; }
    public required string Question { get; init; }
    public required string RequestedBy { get; init; }
}

[EventName("claim.information-provided")]
public sealed record InformationProvided : ClaimEvent
{
    public required string RequestId { get; init; }
}

[EventName("claim.assessed")]
public sealed record ClaimAssessed : ClaimEvent
{
    public required decimal PayableAmount { get; init; }
    public required decimal CoveredPercent { get; init; }
    public required string AssessedBy { get; init; }
    /// <summary>True when the payable amount is above the assessor's authority — a manager must approve.</summary>
    public required bool RequiresManagerApproval { get; init; }
}

[EventName("claim.approved")]
public sealed record ClaimApproved : ClaimEvent
{
    public required decimal ApprovedAmount { get; init; }
    public required string ApprovedBy { get; init; }
}

[EventName("claim.rejected")]
public sealed record ClaimRejected : ClaimEvent
{
    public required string Reason { get; init; }
    public required string RejectedBy { get; init; }
}

[EventName("claim.settled")]
public sealed record ClaimSettled : ClaimEvent
{
    public required decimal PaidAmount { get; init; }
    public required string PaymentReference { get; init; }
}

[EventName("claim.withdrawn")]
public sealed record ClaimWithdrawn : ClaimEvent
{
    public string? Reason { get; init; }
}

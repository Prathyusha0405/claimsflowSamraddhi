namespace ClaimFlow.Contracts;

public enum SlaState { None, OnTrack, DueSoon, Breached }

/// <param name="DueAt">When the current stage must be finished by (null for terminal claims).</param>
/// <param name="Stage">Human label for what is being timed, e.g. "First assignment".</param>
public sealed record SlaStatus(SlaState State, DateTimeOffset? DueAt, string? Stage);

/// <summary>
/// Service-level targets, evaluated identically by the Claims service (UI chips) and Reporting (breach lists)
/// because both work from the same <see cref="ClaimSnapshot"/>.
/// </summary>
public static class Sla
{
    /// <summary>Claims at or above this USD value are treated as High priority.</summary>
    public const decimal HighPriorityUsd = 25_000m;

    /// <summary>How long an unassigned claim may wait before an officer must pick it up.</summary>
    public static TimeSpan FirstTouch(ClaimPriority priority) => priority switch
    {
        ClaimPriority.Urgent => TimeSpan.FromHours(2),
        ClaimPriority.High => TimeSpan.FromHours(4),
        _ => TimeSpan.FromHours(24),
    };

    /// <summary>Target for completing a review once picked up.</summary>
    public static readonly TimeSpan ReviewTarget = TimeSpan.FromDays(5);

    /// <summary>If a claimant has not responded to an information request for this long, staff should chase.</summary>
    public static readonly TimeSpan InformationChase = TimeSpan.FromDays(7);

    /// <summary>Approved claims should be paid within this window.</summary>
    public static readonly TimeSpan SettlementTarget = TimeSpan.FromDays(3);

    public static SlaStatus Evaluate(ClaimSnapshot s, DateTimeOffset now)
    {
        (DateTimeOffset Start, TimeSpan Window, string Stage)? w = s.Status switch
        {
            ClaimStatus.Submitted => (s.SubmittedAt, FirstTouch(s.Priority), "First assignment"),
            ClaimStatus.UnderReview or ClaimStatus.Assessed => (s.FirstAssignedAt ?? s.SubmittedAt, ReviewTarget, "Review"),
            ClaimStatus.AwaitingInformation => (s.AwaitingInfoSince ?? s.UpdatedAt, InformationChase, "Claimant response"),
            ClaimStatus.Approved => (s.DecidedAt ?? s.UpdatedAt, SettlementTarget, "Settlement"),
            _ => null,
        };
        if (w is null) return new SlaStatus(SlaState.None, null, null);

        var due = w.Value.Start + w.Value.Window;
        var remaining = due - now;
        var state = remaining <= TimeSpan.Zero ? SlaState.Breached
            : remaining <= w.Value.Window * 0.25 ? SlaState.DueSoon
            : SlaState.OnTrack;
        return new SlaStatus(state, due, w.Value.Stage);
    }
}

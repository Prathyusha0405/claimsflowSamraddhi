using ClaimFlow.Claims.Domain;
using ClaimFlow.Contracts;

namespace ClaimFlow.Claims.Application;

// ── requests ──────────────────────────────────────────────────────────────────────────────────
public sealed record SubmitClaimRequest(
    string PolicyNumber, ClaimType Type, DateOnly IncidentDate, string IncidentLocation, string Description,
    decimal ClaimedAmount, MotorDetails? Motor, PropertyDetails? Property);
public sealed record InformationRequestBody(string Question);
public sealed record InformationResponseBody(string Response);
public sealed record AssessBody(decimal AssessedLoss, decimal CoveredPercent, string? Notes);
public sealed record ApproveBody(string? Notes);
public sealed record RejectBody(string Reason);
public sealed record SettleBody(string? PaymentReference);
public sealed record AssignBody(string OfficerId);
public sealed record NoteBody(string Text);
public sealed record WithdrawBody(string? Reason);

// ── responses ─────────────────────────────────────────────────────────────────────────────────
public sealed record ClaimSummaryDto(
    string Id, string ClaimNumber, ClaimStatus Status, ClaimPriority Priority, ClaimType Type, Market Market, string Currency,
    string ClaimantId, string ClaimantName, decimal ClaimedAmount, decimal Reserve, decimal ReserveUsd,
    string? AssignedOfficerId, string? AssignedOfficerName, DateTimeOffset SubmittedAt, DateTimeOffset UpdatedAt,
    DateOnly IncidentDate, double AgeHours, SlaStatus Sla, bool AwaitingClaimant, bool NeedsManagerApproval)
{
    public static ClaimSummaryDto From(Claim c, DateTimeOffset now) => new(
        c.Id, c.ClaimNumber, c.Status, c.Priority, c.Type, c.Market, c.Terms.Currency, c.ClaimantId, c.ClaimantName,
        c.ClaimedAmount, c.Reserve, c.ReserveUsd, c.AssignedOfficerId, c.AssignedOfficerName, c.SubmittedAt, c.UpdatedAt,
        c.IncidentDate, Math.Round((now - c.SubmittedAt).TotalHours, 1), ClaimFlow.Contracts.Sla.Evaluate(c.Snapshot(), now),
        c.Status == ClaimStatus.AwaitingInformation,
        c.Status == ClaimStatus.Assessed && c.Assessment is { RequiresManagerApproval: true });
}

public sealed record ClaimDetailDto(
    string Id, string ClaimNumber, int Version, ClaimStatus Status, ClaimPriority Priority, ClaimType Type, Market Market,
    string PolicyNumber, PolicyTerms Policy, string ClaimantId, string ClaimantName,
    DateOnly IncidentDate, string IncidentLocation, string Description, string Currency, decimal ClaimedAmount,
    decimal Reserve, decimal ReserveUsd, decimal UsdRate, MotorDetails? Motor, PropertyDetails? Property,
    string? AssignedOfficerId, string? AssignedOfficerName,
    Assessment? Assessment, Decision? Decision, Settlement? Settlement,
    IReadOnlyList<InformationRequest> InformationRequests, IReadOnlyList<TimelineEntry> Timeline, IReadOnlyList<ClaimNote>? Notes,
    DateTimeOffset SubmittedAt, DateTimeOffset UpdatedAt, SlaStatus Sla,
    IReadOnlyList<string> AllowedActions, IReadOnlyDictionary<string, string> ActionHints)
{
    /// <summary>Builds the view for a specific caller: claimants never receive internal notes or internal assessment remarks.</summary>
    public static ClaimDetailDto For(Claim c, Actor actor, DateTimeOffset now)
    {
        var staff = actor.IsStaff;
        return new ClaimDetailDto(
            c.Id, c.ClaimNumber, c.Version, c.Status, c.Priority, c.Type, c.Market, c.PolicyNumber, c.Terms,
            c.ClaimantId, c.ClaimantName, c.IncidentDate, c.IncidentLocation, c.Description, c.Terms.Currency,
            c.ClaimedAmount, c.Reserve, c.ReserveUsd, Markets.Get(c.Market).UsdRate, c.Motor, c.Property, c.AssignedOfficerId, c.AssignedOfficerName,
            staff ? c.Assessment : c.Assessment is null ? null : c.Assessment with { Notes = null },
            staff ? c.Decision : c.Decision,
            c.Settlement,
            c.InformationRequests,
            c.Timeline,
            staff ? c.Notes : null,
            c.SubmittedAt, c.UpdatedAt, ClaimFlow.Contracts.Sla.Evaluate(c.Snapshot(), now),
            c.AllowedActions(actor), c.ActionHints(actor));
    }
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

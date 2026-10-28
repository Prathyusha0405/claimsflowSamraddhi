using ClaimFlow.Contracts;

namespace ClaimFlow.Claims.Domain;

public enum ClaimAction
{
    PickUp,
    Assign,
    RequestInformation,
    ProvideInformation,
    Assess,
    Approve,
    Reject,
    Settle,
    Withdraw,
}

/// <summary>
/// The single source of truth for which status changes are legal. Table-driven on purpose:
/// it is trivially testable, and the UI's "allowed actions" come from it rather than being re-implemented client-side.
/// </summary>
public static class ClaimStateMachine
{
    private static readonly Dictionary<(ClaimStatus From, ClaimAction Action), ClaimStatus> Map = new()
    {
        // intake → review
        [(ClaimStatus.Submitted, ClaimAction.PickUp)] = ClaimStatus.UnderReview,
        [(ClaimStatus.Submitted, ClaimAction.Assign)] = ClaimStatus.UnderReview,

        // (re)assignment keeps the claim where it is
        [(ClaimStatus.UnderReview, ClaimAction.Assign)] = ClaimStatus.UnderReview,
        [(ClaimStatus.AwaitingInformation, ClaimAction.Assign)] = ClaimStatus.AwaitingInformation,
        [(ClaimStatus.Assessed, ClaimAction.Assign)] = ClaimStatus.Assessed,

        // information loop
        [(ClaimStatus.UnderReview, ClaimAction.RequestInformation)] = ClaimStatus.AwaitingInformation,
        [(ClaimStatus.AwaitingInformation, ClaimAction.ProvideInformation)] = ClaimStatus.UnderReview,

        // assessment → decision
        [(ClaimStatus.UnderReview, ClaimAction.Assess)] = ClaimStatus.Assessed,
        [(ClaimStatus.Assessed, ClaimAction.Approve)] = ClaimStatus.Approved,
        [(ClaimStatus.Assessed, ClaimAction.Reject)] = ClaimStatus.Rejected,
        [(ClaimStatus.UnderReview, ClaimAction.Reject)] = ClaimStatus.Rejected,   // e.g. clearly not covered / fraud

        // payment
        [(ClaimStatus.Approved, ClaimAction.Settle)] = ClaimStatus.Settled,

        // the claimant may walk away until a decision has been made
        [(ClaimStatus.Submitted, ClaimAction.Withdraw)] = ClaimStatus.Withdrawn,
        [(ClaimStatus.UnderReview, ClaimAction.Withdraw)] = ClaimStatus.Withdrawn,
        [(ClaimStatus.AwaitingInformation, ClaimAction.Withdraw)] = ClaimStatus.Withdrawn,
        [(ClaimStatus.Assessed, ClaimAction.Withdraw)] = ClaimStatus.Withdrawn,
    };

    public static IReadOnlyDictionary<(ClaimStatus From, ClaimAction Action), ClaimStatus> Transitions => Map;

    public static bool TryTransition(ClaimStatus from, ClaimAction action, out ClaimStatus to) =>
        Map.TryGetValue((from, action), out to);

    public static IReadOnlyList<ClaimAction> ActionsFrom(ClaimStatus status) =>
        Map.Keys.Where(k => k.From == status).Select(k => k.Action).ToList();

    public static bool IsTerminal(ClaimStatus s) => s is ClaimStatus.Rejected or ClaimStatus.Settled or ClaimStatus.Withdrawn;

    /// <summary>Open claims carry financial exposure (a reserve) until they are paid, rejected or withdrawn.</summary>
    public static bool IsOpen(ClaimStatus s) => !IsTerminal(s);
}

namespace ClaimFlow.Contracts;

/// <summary>The six APAC markets the platform serves.</summary>
public enum Market { SG, HK, MY, TH, ID, AU }

public enum ClaimType { Motor, Property }

/// <summary>Claim lifecycle states. See ClaimStateMachine in the Claims service for the allowed transitions.</summary>
public enum ClaimStatus
{
    Submitted,
    UnderReview,
    AwaitingInformation,
    Assessed,
    Approved,
    Rejected,
    Settled,
    Withdrawn
}

public enum ClaimPriority { Normal, High, Urgent }

public static class Roles
{
    public const string Claimant = "Claimant";
    public const string ClaimsOfficer = "ClaimsOfficer";
    public const string Manager = "Manager";
}

public static class Topics
{
    /// <summary>All claim lifecycle events. Keyed by claimId so Kafka guarantees per-claim ordering.</summary>
    public const string ClaimEvents = "claims.events";
    public const string ClaimEventsDlq = "claims.events.dlq";
}

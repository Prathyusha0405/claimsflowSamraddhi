using System.Text.Json.Serialization;
using ClaimFlow.Contracts;
using ClaimFlow.Platform.Hosting;
using ClaimFlow.Platform.Persistence;
using ClaimFlow.Platform.Security;

namespace ClaimFlow.Claims.Domain;

public sealed record Actor(string Id, string Name, string Role, decimal AuthorityLimitUsd)
{
    public bool IsStaff => Role is Roles.ClaimsOfficer or Roles.Manager;
    public bool IsManager => Role == Roles.Manager;
    public bool IsClaimant => Role == Roles.Claimant;
    public static Actor From(CurrentUser u) => new(u.Id, u.Name, u.Role, u.AuthorityLimitUsd);
}

// ── value objects ─────────────────────────────────────────────────────────────────────────────
public sealed record MotorDetails(string? VehicleRegistration, bool ThirdPartyInvolved, bool InjuryInvolved, string? PoliceReportNumber, bool Drivable);
public sealed record PropertyDetails(string? PropertyAddress, string DamageCause, bool Habitable);
public sealed record InformationRequest(string Id, string Question, string RequestedById, string RequestedByName, DateTimeOffset RequestedAt, string? Response, DateTimeOffset? RespondedAt);
public sealed record ClaimNote(string Id, DateTimeOffset At, string AuthorId, string AuthorName, string Text);
public sealed record TimelineEntry(DateTimeOffset At, string ActorId, string ActorName, string ActorRole, string Action, ClaimStatus? From, ClaimStatus To, string Message);
public sealed record Assessment(decimal AssessedLoss, decimal CoveredPercent, decimal DeductibleApplied, decimal CoverageLimit, decimal PayableAmount, bool RequiresManagerApproval, string? Notes, string AssessedById, string AssessedByName, DateTimeOffset AssessedAt);
public sealed record Decision(bool Approved, string? Notes, string? Reason, decimal Amount, string DecidedById, string DecidedByName, DateTimeOffset DecidedAt);
public sealed record Settlement(string PaymentReference, decimal PaidAmount, string SettledById, string SettledByName, DateTimeOffset SettledAt);

public sealed record SubmitClaimData(
    string PolicyNumber, ClaimType Type, DateOnly IncidentDate, string IncidentLocation, string Description,
    decimal ClaimedAmount, MotorDetails? Motor, PropertyDetails? Property);

/// <summary>
/// The Claim aggregate root. All business rules live here: who may do what, in which state, with which data.
/// Methods never touch I/O; they mutate state, append to the audit timeline and queue integration events that the
/// application layer writes to the outbox in the same transaction as the claim itself.
/// </summary>
public sealed class Claim : IDocument
{
    private readonly List<IntegrationEvent> _events = new();

    [JsonConstructor] private Claim() { }

    [JsonInclude] public string Id { get; private set; } = "";
    [JsonInclude] public string ClaimNumber { get; private set; } = "";
    /// <summary>Increments on every persisted change; carried on events so consumers can drop stale / duplicate ones.</summary>
    [JsonInclude] public int Version { get; private set; }

    [JsonInclude] public string PolicyNumber { get; private set; } = "";
    [JsonInclude] public PolicyTerms Terms { get; private set; } = null!;
    [JsonInclude] public string ClaimantId { get; private set; } = "";
    [JsonInclude] public string ClaimantName { get; private set; } = "";
    [JsonInclude] public Market Market { get; private set; }
    [JsonInclude] public ClaimType Type { get; private set; }

    [JsonInclude] public DateOnly IncidentDate { get; private set; }
    [JsonInclude] public string IncidentLocation { get; private set; } = "";
    [JsonInclude] public string Description { get; private set; } = "";
    [JsonInclude] public decimal ClaimedAmount { get; private set; }
    [JsonInclude] public MotorDetails? Motor { get; private set; }
    [JsonInclude] public PropertyDetails? Property { get; private set; }

    [JsonInclude] public ClaimStatus Status { get; private set; }
    [JsonInclude] public ClaimPriority Priority { get; private set; }
    [JsonInclude] public string? AssignedOfficerId { get; private set; }
    [JsonInclude] public string? AssignedOfficerName { get; private set; }

    /// <summary>Financial exposure: what we currently expect to pay. Zero once paid, rejected or withdrawn.</summary>
    [JsonInclude] public decimal Reserve { get; private set; }
    [JsonInclude] public Assessment? Assessment { get; private set; }
    [JsonInclude] public Decision? Decision { get; private set; }
    [JsonInclude] public Settlement? Settlement { get; private set; }

    [JsonInclude] public List<InformationRequest> InformationRequests { get; private set; } = new();
    [JsonInclude] public List<ClaimNote> Notes { get; private set; } = new();
    [JsonInclude] public List<TimelineEntry> Timeline { get; private set; } = new();

    [JsonInclude] public DateTimeOffset SubmittedAt { get; private set; }
    [JsonInclude] public DateTimeOffset? FirstAssignedAt { get; private set; }
    [JsonInclude] public DateTimeOffset? AwaitingInfoSince { get; private set; }
    [JsonInclude] public DateTimeOffset? DecidedAt { get; private set; }
    [JsonInclude] public DateTimeOffset? SettledAt { get; private set; }
    [JsonInclude] public DateTimeOffset UpdatedAt { get; private set; }

    [JsonIgnore] public decimal PaidAmount => Settlement?.PaidAmount ?? 0;
    [JsonIgnore] public decimal ReserveUsd => Markets.ToUsd(Market, Reserve);
    [JsonIgnore] public InformationRequest? OpenInformationRequest => InformationRequests.LastOrDefault(r => r.RespondedAt is null);

    // ── factory ───────────────────────────────────────────────────────────────────────────────
    public static Claim Submit(Actor claimant, SubmitClaimData d, Market market, PolicyTerms terms, string claimNumber, DateTimeOffset at)
    {
        if (!claimant.IsClaimant) throw new ForbiddenException("FORBIDDEN", "Only claimants can report an incident.");

        var today = DateOnly.FromDateTime(at.UtcDateTime);
        if (d.IncidentDate > today) throw new RuleViolationException("INCIDENT_IN_FUTURE", "The incident date cannot be in the future.");
        if (d.IncidentDate < today.AddYears(-2)) throw new RuleViolationException("INCIDENT_TOO_OLD", "Incidents older than two years can no longer be claimed.");
        Require(!string.IsNullOrWhiteSpace(d.IncidentLocation), "Please tell us where it happened.");
        Require(d.Description?.Trim().Length >= 20, "Please describe what happened in at least 20 characters.");
        Require(d.Description!.Length <= 2000, "The description is too long (max 2000 characters).");
        Require(d.ClaimedAmount > 0, "The claimed amount must be greater than zero.");
        Require(d.ClaimedAmount < 1_000_000_000_000m, "The claimed amount is not realistic.");
        if (d.Type == ClaimType.Motor) Require(d.Motor is not null, "Motor details are required for a motor claim.");
        if (d.Type == ClaimType.Property) Require(d.Property is not null, "Property details are required for a property claim.");

        var c = new Claim
        {
            Id = Guid.NewGuid().ToString(),
            ClaimNumber = claimNumber,
            PolicyNumber = d.PolicyNumber,
            Terms = terms,
            ClaimantId = claimant.Id,
            ClaimantName = claimant.Name,
            Market = market,
            Type = d.Type,
            IncidentDate = d.IncidentDate,
            IncidentLocation = d.IncidentLocation.Trim(),
            Description = d.Description!.Trim(),
            ClaimedAmount = d.ClaimedAmount,
            Motor = d.Type == ClaimType.Motor ? d.Motor : null,
            Property = d.Type == ClaimType.Property ? d.Property : null,
            Status = ClaimStatus.Submitted,
            SubmittedAt = at,
            UpdatedAt = at,
            Version = 1,
        };
        c.Priority = PriorityFor(market, d.ClaimedAmount, c.Motor, c.Property);
        // Initial case reserve: the most we could owe on what was claimed, after the excess and capped by the sum insured.
        c.Reserve = Math.Max(0, Math.Min(d.ClaimedAmount, terms.CoverageLimit) - terms.Deductible);
        c.Timeline.Add(new TimelineEntry(at, claimant.Id, claimant.Name, claimant.Role, "Submitted", null, ClaimStatus.Submitted,
            $"Claim reported for {terms.ProductName.ToLowerInvariant()} ({d.PolicyNumber})."));
        c.Raise(new ClaimSubmitted { Claim = c.Snapshot(), OccurredAt = at });
        return c;
    }

    public static ClaimPriority PriorityFor(Market market, decimal claimedAmount, MotorDetails? motor, PropertyDetails? property)
    {
        if (motor?.InjuryInvolved == true || property is { Habitable: false }) return ClaimPriority.Urgent;   // people first
        if (Markets.ToUsd(market, claimedAmount) >= Sla.HighPriorityUsd) return ClaimPriority.High;
        return ClaimPriority.Normal;
    }

    // ── commands ──────────────────────────────────────────────────────────────────────────────
    public void PickUp(Actor officer, DateTimeOffset at)
    {
        Guard(ClaimAction.PickUp, officer);
        AssignTo(officer.Id, officer.Name, at);
        MoveTo(ClaimAction.PickUp, officer, at, $"Picked up for review by {officer.Name}.");
        Raise(new ClaimAssigned { Claim = Snapshot(), OccurredAt = at, OfficerId = officer.Id, OfficerName = officer.Name, AssignedBy = officer.Name });
    }

    public void Assign(Actor manager, string officerId, string officerName, DateTimeOffset at)
    {
        Guard(ClaimAction.Assign, manager);
        Require(!string.IsNullOrWhiteSpace(officerId), "Choose an officer to assign.");
        if (AssignedOfficerId == officerId) throw new ConflictException("ALREADY_ASSIGNED", $"{officerName} is already handling this claim.");
        var previous = AssignedOfficerId;
        AssignTo(officerId, officerName, at);
        MoveTo(ClaimAction.Assign, manager, at, $"Assigned to {officerName} by {manager.Name}.");
        Raise(new ClaimAssigned { Claim = Snapshot(), OccurredAt = at, OfficerId = officerId, OfficerName = officerName, PreviousOfficerId = previous, AssignedBy = manager.Name });
    }

    public void RequestInformation(Actor officer, string question, DateTimeOffset at)
    {
        Guard(ClaimAction.RequestInformation, officer);
        Require(question?.Trim().Length >= 10, "Please write a clear question (at least 10 characters).");
        var request = new InformationRequest(Guid.NewGuid().ToString("N")[..8], question!.Trim(), officer.Id, officer.Name, at, null, null);
        InformationRequests.Add(request);
        AwaitingInfoSince = at;
        MoveTo(ClaimAction.RequestInformation, officer, at, $"{officer.Name} asked for more information.");
        Raise(new InformationRequested { Claim = Snapshot(), OccurredAt = at, RequestId = request.Id, Question = request.Question, RequestedBy = officer.Name });
    }

    public void ProvideInformation(Actor claimant, string requestId, string response, DateTimeOffset at)
    {
        Guard(ClaimAction.ProvideInformation, claimant);
        var open = InformationRequests.FirstOrDefault(r => r.Id == requestId && r.RespondedAt is null)
                   ?? throw new ConflictException("NO_OPEN_REQUEST", "That information request is no longer open.");
        Require(response?.Trim().Length >= 3, "Please type your answer.");
        InformationRequests[InformationRequests.IndexOf(open)] = open with { Response = response!.Trim(), RespondedAt = at };
        AwaitingInfoSince = null;
        MoveTo(ClaimAction.ProvideInformation, claimant, at, "Claimant provided the requested information.");
        Raise(new InformationProvided { Claim = Snapshot(), OccurredAt = at, RequestId = requestId });
    }

    /// <summary>
    /// payable = min( assessedLoss × covered% − excess , sum insured ), never below zero.
    /// The reserve is re-set to the payable amount: exposure becomes what we actually expect to pay.
    /// </summary>
    public static decimal CalculatePayable(decimal assessedLoss, decimal coveredPercent, decimal deductible, decimal coverageLimit) =>
        Math.Round(Math.Min(Math.Max(0, assessedLoss * coveredPercent / 100m - deductible), coverageLimit), 2);

    public void Assess(Actor officer, decimal assessedLoss, decimal coveredPercent, string? notes, DateTimeOffset at)
    {
        Guard(ClaimAction.Assess, officer);
        Require(assessedLoss >= 0 && assessedLoss < 1_000_000_000_000m, "The assessed loss must be zero or more.");
        Require(coveredPercent is >= 0 and <= 100, "Covered percentage must be between 0 and 100.");

        var payable = CalculatePayable(assessedLoss, coveredPercent, Terms.Deductible, Terms.CoverageLimit);
        var needsManager = !officer.IsManager && Markets.ToUsd(Market, payable) > officer.AuthorityLimitUsd;
        Assessment = new Assessment(assessedLoss, coveredPercent, Terms.Deductible, Terms.CoverageLimit, payable, needsManager,
            string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(), officer.Id, officer.Name, at);
        Reserve = payable;
        MoveTo(ClaimAction.Assess, officer, at, needsManager
            ? $"Assessment completed by {officer.Name}; payable {Terms.Currency} {payable:N2} needs manager approval."
            : $"Assessment completed by {officer.Name}.");
        Raise(new ClaimAssessed { Claim = Snapshot(), OccurredAt = at, PayableAmount = payable, CoveredPercent = coveredPercent, AssessedBy = officer.Name, RequiresManagerApproval = needsManager });
    }

    public void Approve(Actor actor, string? notes, DateTimeOffset at)
    {
        Guard(ClaimAction.Approve, actor);
        var payable = Assessment!.PayableAmount;
        Decision = new Decision(true, string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(), null, payable, actor.Id, actor.Name, at);
        DecidedAt = at;
        MoveTo(ClaimAction.Approve, actor, at, $"Approved for {Terms.Currency} {payable:N2} by {actor.Name}.");
        Raise(new ClaimApproved { Claim = Snapshot(), OccurredAt = at, ApprovedAmount = payable, ApprovedBy = actor.Name });
    }

    public void Reject(Actor actor, string reason, DateTimeOffset at)
    {
        Guard(ClaimAction.Reject, actor);
        Require(reason?.Trim().Length >= 10, "Please give a reason the claimant can understand (at least 10 characters).");
        Decision = new Decision(false, null, reason!.Trim(), 0, actor.Id, actor.Name, at);
        DecidedAt = at;
        Reserve = 0;
        MoveTo(ClaimAction.Reject, actor, at, $"Claim declined by {actor.Name}.");
        Raise(new ClaimRejected { Claim = Snapshot(), OccurredAt = at, Reason = Decision.Reason!, RejectedBy = actor.Name });
    }

    public void Settle(Actor actor, string paymentReference, DateTimeOffset at)
    {
        Guard(ClaimAction.Settle, actor);
        Require(!string.IsNullOrWhiteSpace(paymentReference), "A payment reference is required.");
        var paid = Decision!.Amount;
        Settlement = new Settlement(paymentReference.Trim(), paid, actor.Id, actor.Name, at);
        SettledAt = at;
        Reserve = 0;   // exposure is released: the money has left the building
        MoveTo(ClaimAction.Settle, actor, at, $"Payment of {Terms.Currency} {paid:N2} issued (ref {paymentReference.Trim()}).");
        Raise(new ClaimSettled { Claim = Snapshot(), OccurredAt = at, PaidAmount = paid, PaymentReference = paymentReference.Trim() });
    }

    public void Withdraw(Actor claimant, string? reason, DateTimeOffset at)
    {
        Guard(ClaimAction.Withdraw, claimant);
        Reserve = 0;
        MoveTo(ClaimAction.Withdraw, claimant, at, "Claim withdrawn by the claimant.");
        Raise(new ClaimWithdrawn { Claim = Snapshot(), OccurredAt = at, Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim() });
    }

    /// <summary>Internal staff note. Not a status change, so no event; never visible to claimants.</summary>
    public void AddNote(Actor staff, string text, DateTimeOffset at)
    {
        if (!staff.IsStaff) throw new ForbiddenException("FORBIDDEN", "Only claims staff can add internal notes.");
        Require(text?.Trim().Length >= 2, "The note is empty.");
        Notes.Add(new ClaimNote(Guid.NewGuid().ToString("N")[..8], at, staff.Id, staff.Name, text!.Trim()));
        Version++;
        UpdatedAt = at;
    }

    // ── permissions: what can THIS actor do to THIS claim right now? ──────────────────────────
    /// <summary>
    /// Returns null if allowed, otherwise the exception that would be thrown. Order: who you are (403) → what state we're in (409)
    /// → business rule (422). Shared by the guard and the "allowed actions" the API sends to the UI.
    /// </summary>
    public AppException? Check(ClaimAction action, Actor actor)
    {
        switch (action)
        {
            case ClaimAction.PickUp:
                if (!actor.IsStaff) return new ForbiddenException("FORBIDDEN", "Only claims staff can pick up claims.");
                if (AssignedOfficerId is not null)
                    return new ConflictException("ALREADY_ASSIGNED", $"This claim was already picked up by {AssignedOfficerName}.");
                break;
            case ClaimAction.Assign:
                if (!actor.IsManager) return new ForbiddenException("FORBIDDEN", "Only managers can (re)assign claims.");
                break;
            case ClaimAction.ProvideInformation:
            case ClaimAction.Withdraw:
                if (!actor.IsClaimant || actor.Id != ClaimantId)
                    return new ForbiddenException("FORBIDDEN", "Only the claimant who reported this claim can do that.");
                break;
            default:
                if (!actor.IsStaff) return new ForbiddenException("FORBIDDEN", "Only claims staff can do that.");
                if (!actor.IsManager && AssignedOfficerId != actor.Id)
                    return new ForbiddenException("NOT_ASSIGNED_TO_YOU",
                        AssignedOfficerId is null ? "Pick this claim up first." : $"This claim is handled by {AssignedOfficerName}. Only they or a manager can act on it.");
                break;
        }

        if (!ClaimStateMachine.TryTransition(Status, action, out _))
            return new ConflictException("INVALID_STATE", $"You can't {Describe(action)} a claim that is {Humanise(Status)}.");

        if (action == ClaimAction.Approve && !actor.IsManager)
        {
            var payableUsd = Markets.ToUsd(Market, Assessment!.PayableAmount);
            if (payableUsd > actor.AuthorityLimitUsd)
                return new RuleViolationException("AUTHORITY_LIMIT",
                    $"The payable amount (≈ USD {payableUsd:N0}) is above your approval authority (USD {actor.AuthorityLimitUsd:N0}). A manager must approve it.");
        }
        return null;
    }

    private void Guard(ClaimAction action, Actor actor)
    {
        var error = Check(action, actor);
        if (error is not null) throw error;
    }

    /// <summary>Actions currently available to the actor (drives the UI buttons).</summary>
    public IReadOnlyList<string> AllowedActions(Actor actor)
    {
        var list = Enum.GetValues<ClaimAction>().Where(a => Check(a, actor) is null).Select(a => a.ToString()).ToList();
        if (actor.IsStaff) list.Add("AddNote");
        return list;
    }

    /// <summary>Why an action that "should" be possible is blocked by a business rule (so the UI can explain instead of hiding).</summary>
    public IReadOnlyDictionary<string, string> ActionHints(Actor actor)
    {
        var hints = new Dictionary<string, string>();
        foreach (var a in Enum.GetValues<ClaimAction>())
            if (Check(a, actor) is RuleViolationException ex) hints[a.ToString()] = ex.Message;
        return hints;
    }

    // ── internals ─────────────────────────────────────────────────────────────────────────────
    public IReadOnlyList<IntegrationEvent> DequeueEvents()
    {
        var copy = _events.ToList();
        _events.Clear();
        return copy;
    }

    public ClaimSnapshot Snapshot() => new(
        Id, ClaimNumber, Version, PolicyNumber, ClaimantId, ClaimantName, Market, Type, Status, Priority,
        Terms.Currency, ClaimedAmount, Reserve, Decision is { Approved: true } ? Decision.Amount : null, PaidAmount,
        AssignedOfficerId, AssignedOfficerName, SubmittedAt, FirstAssignedAt, AwaitingInfoSince, DecidedAt, SettledAt, UpdatedAt);

    private void AssignTo(string officerId, string officerName, DateTimeOffset at)
    {
        AssignedOfficerId = officerId;
        AssignedOfficerName = officerName;
        FirstAssignedAt ??= at;
    }

    private void MoveTo(ClaimAction action, Actor actor, DateTimeOffset at, string message)
    {
        ClaimStateMachine.TryTransition(Status, action, out var next);
        var from = Status;
        Status = next;
        Version++;
        UpdatedAt = at;
        Timeline.Add(new TimelineEntry(at, actor.Id, actor.Name, actor.Role, action.ToString(), from, next, message));
    }

    private void Raise(IntegrationEvent evt) => _events.Add(evt);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new BadRequestException("VALIDATION_ERROR", message);
    }

    private static string Describe(ClaimAction a) => a switch
    {
        ClaimAction.PickUp => "pick up",
        ClaimAction.Assign => "reassign",
        ClaimAction.RequestInformation => "request information on",
        ClaimAction.ProvideInformation => "provide information for",
        ClaimAction.Assess => "assess",
        ClaimAction.Approve => "approve",
        ClaimAction.Reject => "reject",
        ClaimAction.Settle => "settle",
        ClaimAction.Withdraw => "withdraw",
        _ => a.ToString().ToLowerInvariant(),
    };

    private static string Humanise(ClaimStatus s) => s switch
    {
        ClaimStatus.UnderReview => "under review",
        ClaimStatus.AwaitingInformation => "awaiting information",
        _ => s.ToString().ToLowerInvariant(),
    };
}

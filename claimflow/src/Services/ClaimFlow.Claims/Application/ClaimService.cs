using ClaimFlow.Claims.Domain;
using ClaimFlow.Contracts;
using ClaimFlow.Platform.Hosting;
using ClaimFlow.Platform.Messaging;
using ClaimFlow.Platform.Persistence;

namespace ClaimFlow.Claims.Application;

/// <summary>
/// Application service: loads aggregates, runs the domain method, saves the claim AND its events in ONE store transaction
/// (transactional outbox), so a claim can never change without its event being recorded — and vice versa.
/// Slow I/O (the policy check) happens before the transaction opens; nothing slow runs while the lock is held.
/// </summary>
public sealed class ClaimService(
    JsonDataStore store,
    DocumentCollection<Claim> claims,
    OutboxWriter outbox,
    IPolicyClient policies,
    StaffDirectory staff,
    TimeProvider clock,
    ILogger<ClaimService> log)
{
    private DateTimeOffset Now => clock.GetUtcNow();

    // ── commands ──────────────────────────────────────────────────────────────────────────────
    public async Task<Claim> SubmitAsync(Actor actor, SubmitClaimRequest req, CancellationToken ct)
    {
        if (!actor.IsClaimant) throw new ForbiddenException("FORBIDDEN", "Only claimants can report an incident.");
        if (string.IsNullOrWhiteSpace(req.PolicyNumber)) throw new BadRequestException("VALIDATION_ERROR", "Please choose a policy.");

        var coverage = await policies.CheckCoverageAsync(req.PolicyNumber.Trim(), req.IncidentDate, req.Type, ct);
        if (coverage.ClaimantId is not null && coverage.ClaimantId != actor.Id)
            throw new ForbiddenException("POLICY_NOT_OWNED", "That policy does not belong to you.");
        if (!coverage.Eligible)
            throw new RuleViolationException(coverage.Code ?? "POLICY_NOT_ELIGIBLE", coverage.Reason ?? "This policy cannot be used for a claim.");

        return CreateClaim(actor, req, coverage.Market!.Value, coverage.Terms!, Now);
    }

    public Claim CreateClaim(Actor actor, SubmitClaimRequest req, Market market, PolicyTerms terms, DateTimeOffset at) =>
        store.Transaction(() =>
        {
            // Business rule: one open claim per incident per policy.
            var duplicate = claims.ByIndex("claimant", actor.Id).FirstOrDefault(c =>
                c.PolicyNumber == req.PolicyNumber && c.IncidentDate == req.IncidentDate && c.Type == req.Type && ClaimStateMachine.IsOpen(c.Status));
            if (duplicate is not null)
                throw new ConflictException("DUPLICATE_CLAIM", $"You already have an open claim ({duplicate.ClaimNumber}) for this incident.");

            var seq = store.NextSequence($"claim-number-{market}-{at.Year}");
            var number = $"CLM-{market}-{at.Year}-{seq:D6}";
            var data = new SubmitClaimData(req.PolicyNumber.Trim(), req.Type, req.IncidentDate, req.IncidentLocation ?? "", req.Description ?? "", req.ClaimedAmount, req.Motor, req.Property);
            var claim = Claim.Submit(actor, data, market, terms, number, at);
            Save(claim);
            log.LogInformation("Claim {Number} submitted by {Claimant} · {Type} · priority {Priority}", number, actor.Name, req.Type, claim.Priority);
            return claim;
        });

    public Claim PickUp(Actor actor, string id, DateTimeOffset? at = null) => Mutate(id, c => c.PickUp(actor, at ?? Now));

    public Claim Assign(Actor actor, string id, string officerId, DateTimeOffset? at = null)
    {
        var officer = staff.Find(officerId) ?? throw new NotFoundException("OFFICER_NOT_FOUND", "That staff member does not exist.");
        return Mutate(id, c => c.Assign(actor, officer.Id, officer.Name, at ?? Now));
    }

    public Claim RequestInformation(Actor actor, string id, string question, DateTimeOffset? at = null) =>
        Mutate(id, c => c.RequestInformation(actor, question, at ?? Now));

    public Claim ProvideInformation(Actor actor, string id, string requestId, string response, DateTimeOffset? at = null) =>
        Mutate(id, c => c.ProvideInformation(actor, requestId, response, at ?? Now));

    public Claim Assess(Actor actor, string id, decimal loss, decimal coveredPercent, string? notes, DateTimeOffset? at = null) =>
        Mutate(id, c => c.Assess(actor, loss, coveredPercent, notes, at ?? Now));

    public Claim Approve(Actor actor, string id, string? notes, DateTimeOffset? at = null) => Mutate(id, c => c.Approve(actor, notes, at ?? Now));

    public Claim Reject(Actor actor, string id, string reason, DateTimeOffset? at = null) => Mutate(id, c => c.Reject(actor, reason, at ?? Now));

    public Claim Settle(Actor actor, string id, string? paymentReference, DateTimeOffset? when = null) => Mutate(id, c =>
    {
        var t = when ?? Now;
        var reference = string.IsNullOrWhiteSpace(paymentReference) ? $"PAY-{c.ClaimNumber[^6..]}-{t:yyMMdd}" : paymentReference;
        c.Settle(actor, reference, t);
    });

    public Claim Withdraw(Actor actor, string id, string? reason, DateTimeOffset? at = null) => Mutate(id, c => c.Withdraw(actor, reason, at ?? Now));

    public Claim AddNote(Actor actor, string id, string text, DateTimeOffset? at = null) => Mutate(id, c => c.AddNote(actor, text, at ?? Now));

    // ── queries ───────────────────────────────────────────────────────────────────────────────
    public Claim Get(Actor actor, string idOrNumber)
    {
        var claim = Find(idOrNumber);
        // Claimants asking for someone else's claim get a 404, not a 403 — don't confirm it exists.
        if (actor.IsClaimant && claim.ClaimantId != actor.Id) throw new NotFoundException("CLAIM_NOT_FOUND", "We couldn't find that claim.");
        return claim;
    }

    public IReadOnlyList<Claim> Mine(Actor actor) =>
        claims.ByIndex("claimant", actor.Id).OrderByDescending(c => c.SubmittedAt).ToList();

    /// <summary>Unassigned claims, most urgent first, then oldest first — the officer's "what should I pick up next" list.</summary>
    public IReadOnlyList<Claim> Queue(Market? market, ClaimType? type, ClaimPriority? priority) =>
        claims.ByIndex("status", ClaimStatus.Submitted.ToString())
            .Where(c => c.AssignedOfficerId is null)
            .Where(c => market is null || c.Market == market)
            .Where(c => type is null || c.Type == type)
            .Where(c => priority is null || c.Priority == priority)
            .OrderByDescending(c => c.Priority).ThenBy(c => c.SubmittedAt).ToList();

    /// <summary>An officer's open workload, via the "assignee" index (no table scan).</summary>
    public IReadOnlyList<Claim> MyWork(Actor actor) =>
        claims.ByIndex("assignee", actor.Id).Where(c => ClaimStateMachine.IsOpen(c.Status))
            .OrderByDescending(c => c.Priority).ThenBy(c => c.SubmittedAt).ToList();

    public PagedResult<Claim> Search(ClaimStatus? status, string? officerId, Market? market, ClaimType? type, string? text, int page, int pageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var term = text?.Trim();
        var matches = claims.Query(c =>
                (status is null || c.Status == status) &&
                (officerId is null || c.AssignedOfficerId == officerId) &&
                (market is null || c.Market == market) &&
                (type is null || c.Type == type) &&
                (string.IsNullOrEmpty(term) ||
                 c.ClaimNumber.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                 c.ClaimantName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                 c.PolicyNumber.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(c => c.SubmittedAt).ToList();
        return new PagedResult<Claim>(matches.Skip((page - 1) * pageSize).Take(pageSize).ToList(), matches.Count, page, pageSize);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────
    private Claim Find(string idOrNumber) =>
        claims.Get(idOrNumber)
        ?? claims.ByIndex("number", idOrNumber.ToUpperInvariant()).FirstOrDefault()
        ?? throw new NotFoundException("CLAIM_NOT_FOUND", "We couldn't find that claim.");

    private Claim Mutate(string idOrNumber, Action<Claim> change) => store.Transaction(() =>
    {
        var claim = Find(idOrNumber);
        change(claim);
        Save(claim);
        return claim;
    });

    private void Save(Claim claim)
    {
        claims.Upsert(claim);
        foreach (var evt in claim.DequeueEvents()) outbox.Enqueue(evt, claim.Id);
    }
}

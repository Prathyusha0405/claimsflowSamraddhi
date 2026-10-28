using ClaimFlow.Claims.Application;
using ClaimFlow.Claims.Domain;
using ClaimFlow.Contracts;
using ClaimFlow.Platform.Persistence;

namespace ClaimFlow.Claims.Seed;

/// <summary>
/// Populates an empty store with ~40 claims across all six markets and every lifecycle stage, by driving the REAL domain
/// methods with back-dated timestamps (so history, SLA breaches, exposure and the event stream all look realistic).
/// Every seeded change goes through the outbox — Reporting and Notifications fill themselves from the events.
/// </summary>
public sealed class DemoDataSeeder(ClaimService svc, DocumentCollection<Claim> claims, TimeProvider clock, ILogger<DemoDataSeeder> log)
{
    private enum Stage { Submitted, UnderReview, AwaitingInfo, Assessed, Approved, Settled, Rejected, Withdrawn }

    private sealed record Plan(int Claimant, ClaimType Type, double AgeHours, Stage Stage, int Officer = 0, int ClaimedUsd = 3000,
        bool Injury = false, bool Uninhabitable = false, bool InfoLoop = false, double CoveredPercent = 100, double LossFactor = 0.92, bool Note = false);

    private static readonly Plan[] Plans =
    [
        // ── waiting in the queue (some already breaching their first-touch SLA) ──
        new(0, ClaimType.Motor, 0.7, Stage.Submitted, ClaimedUsd: 2400),
        new(1, ClaimType.Property, 1.5, Stage.Submitted, ClaimedUsd: 6200),
        new(2, ClaimType.Motor, 3, Stage.Submitted, ClaimedUsd: 1800),
        new(3, ClaimType.Motor, 5, Stage.Submitted, ClaimedUsd: 9300, Injury: true),
        new(4, ClaimType.Property, 30, Stage.Submitted, ClaimedUsd: 41000),
        new(5, ClaimType.Motor, 55, Stage.Submitted, ClaimedUsd: 3100),
        new(0, ClaimType.Property, 9, Stage.Submitted, ClaimedUsd: 18500, Uninhabitable: true),

        // ── under review ──
        new(1, ClaimType.Motor, 20, Stage.UnderReview, 0, 2800, Note: true),
        new(2, ClaimType.Property, 44, Stage.UnderReview, 1, 12500, InfoLoop: true),
        new(3, ClaimType.Motor, 70, Stage.UnderReview, 0, 5200),
        new(4, ClaimType.Motor, 96, Stage.UnderReview, 2, 1900, Note: true),
        new(5, ClaimType.Property, 120, Stage.UnderReview, 1, 26000),
        new(0, ClaimType.Motor, 30, Stage.UnderReview, 2, 4300, InfoLoop: true),
        new(2, ClaimType.Motor, 150, Stage.UnderReview, 0, 7400),
        new(3, ClaimType.Property, 8, Stage.UnderReview, 1, 15800),

        // ── waiting on the claimant ──
        new(1, ClaimType.Property, 60, Stage.AwaitingInfo, 0, 9100),
        new(4, ClaimType.Motor, 100, Stage.AwaitingInfo, 1, 2600),
        new(5, ClaimType.Motor, 300, Stage.AwaitingInfo, 2, 3500),
        new(0, ClaimType.Property, 36, Stage.AwaitingInfo, 2, 21000),

        // ── assessed, waiting for approval (several above the officer's authority) ──
        new(2, ClaimType.Motor, 110, Stage.Assessed, 0, 3800, LossFactor: 0.9),
        new(3, ClaimType.Property, 130, Stage.Assessed, 1, 52000, CoveredPercent: 90),
        new(5, ClaimType.Property, 90, Stage.Assessed, 0, 38000),
        new(1, ClaimType.Motor, 75, Stage.Assessed, 2, 8800),
        new(4, ClaimType.Property, 160, Stage.Assessed, 2, 14000, CoveredPercent: 80),

        // ── approved, payment pending ──
        new(0, ClaimType.Motor, 200, Stage.Approved, 1, 5800),
        new(1, ClaimType.Property, 260, Stage.Approved, 0, 22000),
        new(4, ClaimType.Motor, 170, Stage.Approved, 0, 2100),
        new(5, ClaimType.Property, 320, Stage.Approved, 2, 11000),

        // ── settled (history for performance stats) ──
        new(2, ClaimType.Motor, 420, Stage.Settled, 0, 3300, InfoLoop: true),
        new(3, ClaimType.Property, 480, Stage.Settled, 1, 17500),
        new(0, ClaimType.Motor, 520, Stage.Settled, 2, 2600),
        new(4, ClaimType.Motor, 560, Stage.Settled, 1, 6100),
        new(1, ClaimType.Property, 610, Stage.Settled, 0, 8900, InfoLoop: true),
        new(5, ClaimType.Motor, 690, Stage.Settled, 2, 1500),

        // ── declined ──
        new(2, ClaimType.Property, 380, Stage.Rejected, 0, 7200),
        new(3, ClaimType.Motor, 450, Stage.Rejected, 1, 4800),
        new(5, ClaimType.Motor, 540, Stage.Rejected, 2, 2900),
        new(0, ClaimType.Property, 640, Stage.Rejected, 1, 13000),

        // ── withdrawn ──
        new(1, ClaimType.Motor, 220, Stage.Withdrawn, ClaimedUsd: 1700),
        new(4, ClaimType.Property, 330, Stage.Withdrawn, ClaimedUsd: 5400),
    ];

    private static readonly string[] MotorStories =
    [
        "Rear-ended at a traffic light during the evening rush; bumper, boot lid and rear sensors damaged.",
        "Side-swiped by a van while parked overnight on the street; driver door and mirror need replacing.",
        "Collision at a busy junction — the other driver admitted fault. Front-left wing and headlamp damaged.",
        "Windscreen cracked by a stone thrown up from a lorry on the expressway; chip spread across the driver's view.",
        "Reversed into a pillar in a car park; rear bumper cracked and a parking sensor dislodged.",
        "Heavy rain caused aquaplaning; the car clipped the central barrier and the front bumper was damaged.",
    ];

    private static readonly (string Cause, string Story)[] PropertyStories =
    [
        ("Water", "A burst pipe under the kitchen sink flooded the kitchen and hallway overnight; flooring and cabinets are ruined."),
        ("Storm", "A storm lifted several roof tiles and rain leaked into the master bedroom ceiling."),
        ("Theft", "Break-in while we were away; the back door was forced and a laptop, television and jewellery were taken."),
        ("Fire", "A small kitchen fire caused by an electrical fault; smoke damage across the living area and the extractor hood."),
        ("Water", "An air-conditioner drain overflowed into the bedroom wall; paint peeling and the wardrobe base swollen."),
    ];

    private static readonly Dictionary<Market, string[]> Places = new()
    {
        [Market.SG] = ["PIE near Toa Payoh", "Orchard Road car park", "Tampines Ave 5"],
        [Market.HK] = ["Cross-Harbour Tunnel approach", "Kwun Tong Road", "Sha Tin Racecourse car park"],
        [Market.MY] = ["Jalan Tun Razak, Kuala Lumpur", "Federal Highway near Subang", "Bangsar South"],
        [Market.TH] = ["Rama IV Road, Bangkok", "Sukhumvit Soi 24", "Chaengwattana Road"],
        [Market.ID] = ["Jl. Sudirman, Jakarta", "Tol Jagorawi KM 12", "Kemang Raya"],
        [Market.AU] = ["Parramatta Road, Sydney", "M1 near Gold Coast", "King Street, Newtown"],
    };

    private static readonly string[] RejectReasons =
    [
        "The damage is the result of gradual wear and tear, which is excluded under section 4 of your policy wording.",
        "The incident date falls outside the cover provided, and no supporting evidence was supplied to show otherwise.",
        "Our assessor found the damage inconsistent with the circumstances described, so we cannot accept the claim.",
        "The loss is below your policy excess, so no payment is due on this claim.",
    ];

    public void SeedIfEmpty()
    {
        if (claims.Count() > 0) return;

        var policies = DemoCatalog.BuildPolicies(DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
        var manager = DemoCatalog.Users.First(u => u.Role == Roles.Manager);
        var managerActor = new Actor(manager.Id, manager.Name, manager.Role, manager.AuthorityLimitUsd);
        var officers = DemoCatalog.Officers.Select(o => new Actor(o.Id, o.Name, o.Role, o.AuthorityLimitUsd)).ToArray();
        var done = 0;

        for (var i = 0; i < Plans.Length; i++)
        {
            try { Run(Plans[i], i, policies, officers, managerActor); done++; }
            catch (Exception ex) { log.LogWarning("Skipped demo claim #{Index}: {Message}", i, ex.Message); }
        }
        log.LogInformation("Seeded {Count} demo claims covering every lifecycle stage", done);
    }

    private void Run(Plan p, int i, IReadOnlyList<DemoPolicy> policies, Actor[] officers, Actor manager)
    {
        var now = clock.GetUtcNow();
        var t0 = now - TimeSpan.FromHours(p.AgeHours);
        DateTimeOffset At(double f) => t0 + (now - t0) * f;

        var holder = DemoCatalog.Claimants[p.Claimant % DemoCatalog.Claimants.Count];
        var policy = policies.First(x => x.ClaimantId == holder.Id && x.Product == p.Type && x.Status == "Active");
        var claimant = new Actor(holder.Id, holder.Name, Roles.Claimant, 0);
        var terms = new PolicyTerms(policy.ProductName, policy.Currency, policy.CoverageLimit, policy.Deductible,
            policy.EffectiveFrom, policy.EffectiveTo, policy.AssetDescription);
        var claimed = Markets.FromUsd(policy.Market, p.ClaimedUsd);
        var place = Places[policy.Market][i % 3];
        var incident = DateOnly.FromDateTime((t0 - TimeSpan.FromDays(1 + i)).UtcDateTime);   // unique per plan → no duplicate-claim clashes

        MotorDetails? motor = null;
        PropertyDetails? property = null;
        string story;
        if (p.Type == ClaimType.Motor)
        {
            story = MotorStories[i % MotorStories.Length];
            motor = new MotorDetails(policy.VehicleRegistration, i % 3 == 0, p.Injury, i % 2 == 0 ? $"PR-{policy.Market}-{4100 + i}" : null, i % 4 != 0);
        }
        else
        {
            var s = PropertyStories[i % PropertyStories.Length];
            story = s.Story;
            property = new PropertyDetails(policy.PropertyAddress, s.Cause, !p.Uninhabitable);
        }

        var claim = svc.CreateClaim(claimant, new SubmitClaimRequest(policy.PolicyNumber, p.Type, incident, place, story, claimed, motor, property),
            policy.Market, terms, t0);
        if (p.Stage == Stage.Submitted) return;

        if (p.Stage == Stage.Withdrawn)
        {
            svc.Withdraw(claimant, claim.Id, "I sorted this out privately with the other party.", At(0.4));
            return;
        }

        var officer = officers[p.Officer % officers.Length];
        svc.PickUp(officer, claim.Id, At(0.12));
        if (p.Note) svc.AddNote(officer, claim.Id, "Photos received. Requested a repair estimate from the panel workshop.", At(0.2));

        if (p.Stage == Stage.AwaitingInfo)
        {
            svc.RequestInformation(officer, claim.Id, "Could you upload the repair quotation or a clear photo of the damage from two angles?", At(0.3));
            return;
        }

        if (p.InfoLoop || p.Stage == Stage.UnderReview && p.InfoLoop)
        {
            var withRequest = svc.RequestInformation(officer, claim.Id, "Please confirm the exact time of the incident and whether anyone else was present.", At(0.25));
            var requestId = withRequest.InformationRequests.Last().Id;
            svc.ProvideInformation(claimant, claim.Id, requestId, "It happened at about 7:40pm. My neighbour witnessed it and can confirm.", At(0.4));
        }
        if (p.Stage == Stage.UnderReview) return;

        var loss = Math.Round(claimed * (decimal)p.LossFactor, 0);
        var assessed = svc.Assess(officer, claim.Id, loss, (decimal)p.CoveredPercent, "Assessed against the repair estimate and policy terms.", At(0.55));
        if (p.Stage == Stage.Assessed) return;

        if (p.Stage == Stage.Rejected)
        {
            svc.Reject(officer, claim.Id, RejectReasons[i % RejectReasons.Length], At(0.7));
            return;
        }

        var payableUsd = Markets.ToUsd(policy.Market, assessed.Assessment!.PayableAmount);
        var approver = payableUsd <= officer.AuthorityLimitUsd ? officer : manager;
        svc.Approve(approver, claim.Id, "Within policy terms.", At(0.7));
        if (p.Stage == Stage.Approved) return;

        svc.Settle(officer, claim.Id, null, At(0.85));
    }
}

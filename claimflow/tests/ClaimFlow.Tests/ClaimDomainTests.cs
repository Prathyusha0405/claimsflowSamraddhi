using ClaimFlow.Claims.Application;
using ClaimFlow.Claims.Domain;
using ClaimFlow.Contracts;
using ClaimFlow.Platform.Hosting;

namespace ClaimFlow.Tests;

internal static class Fx
{
    public static readonly DateTimeOffset T0 = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    public static Actor Claimant(string id = "c-sg-001") => new(id, "Mei Tan", Roles.Claimant, 0);
    public static Actor Officer(string id = "o-001", int authorityUsd = 10_000) => new(id, "Officer " + id, Roles.ClaimsOfficer, authorityUsd);
    public static Actor Manager() => new("m-001", "Sofia Reyes", Roles.Manager, DemoCatalog.Unlimited);

    /// <summary>SGD policy: limit 100,000, excess 1,000. (SGD→USD = 0.74)</summary>
    public static PolicyTerms Terms() => new("Comprehensive Motor", "SGD", 100_000m, 1_000m,
        new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "2022 Toyota Corolla — SLX 4821 P");

    public static Claim NewClaim(decimal claimed = 5_000m, bool injury = false, Actor? claimant = null, DateTimeOffset? at = null)
    {
        var data = new SubmitClaimData("SG-MTR-100201", ClaimType.Motor, new DateOnly(2026, 9, 28), "PIE exit 12",
            "Another car merged into my lane and scraped the passenger side.", claimed,
            new MotorDetails("SLX 4821 P", true, injury, null, true), null);
        return Claim.Submit(claimant ?? Claimant(), data, Market.SG, Terms(), "CLM-SG-2026-000001", at ?? T0);
    }

    /// <summary>Submitted → picked up → assessed, ready for a decision.</summary>
    public static Claim Assessed(Actor officer, decimal loss, decimal covered = 100m)
    {
        var c = NewClaim(loss);
        c.PickUp(officer, T0.AddHours(1));
        c.Assess(officer, loss, covered, "ok", T0.AddHours(2));
        return c;
    }
}

public class StateMachineTests
{
    [Theory]
    [InlineData(ClaimStatus.Submitted, ClaimAction.PickUp, ClaimStatus.UnderReview)]
    [InlineData(ClaimStatus.UnderReview, ClaimAction.RequestInformation, ClaimStatus.AwaitingInformation)]
    [InlineData(ClaimStatus.AwaitingInformation, ClaimAction.ProvideInformation, ClaimStatus.UnderReview)]
    [InlineData(ClaimStatus.UnderReview, ClaimAction.Assess, ClaimStatus.Assessed)]
    [InlineData(ClaimStatus.Assessed, ClaimAction.Approve, ClaimStatus.Approved)]
    [InlineData(ClaimStatus.Assessed, ClaimAction.Reject, ClaimStatus.Rejected)]
    [InlineData(ClaimStatus.UnderReview, ClaimAction.Reject, ClaimStatus.Rejected)]
    [InlineData(ClaimStatus.Approved, ClaimAction.Settle, ClaimStatus.Settled)]
    [InlineData(ClaimStatus.Assessed, ClaimAction.Withdraw, ClaimStatus.Withdrawn)]
    public void Legal_transitions_are_allowed(ClaimStatus from, ClaimAction action, ClaimStatus expected)
    {
        Assert.True(ClaimStateMachine.TryTransition(from, action, out var to));
        Assert.Equal(expected, to);
    }

    [Theory]
    [InlineData(ClaimStatus.Submitted, ClaimAction.Approve)]
    [InlineData(ClaimStatus.Submitted, ClaimAction.Assess)]
    [InlineData(ClaimStatus.UnderReview, ClaimAction.Settle)]
    [InlineData(ClaimStatus.AwaitingInformation, ClaimAction.Assess)]
    [InlineData(ClaimStatus.Approved, ClaimAction.Withdraw)]
    [InlineData(ClaimStatus.Approved, ClaimAction.Reject)]
    [InlineData(ClaimStatus.Settled, ClaimAction.Reject)]
    [InlineData(ClaimStatus.Rejected, ClaimAction.Approve)]
    public void Illegal_transitions_are_refused(ClaimStatus from, ClaimAction action)
    {
        Assert.False(ClaimStateMachine.TryTransition(from, action, out _));
    }

    [Fact]
    public void Terminal_statuses_have_no_way_out()
    {
        foreach (var terminal in new[] { ClaimStatus.Rejected, ClaimStatus.Settled, ClaimStatus.Withdrawn })
        {
            Assert.True(ClaimStateMachine.IsTerminal(terminal));
            Assert.Empty(ClaimStateMachine.ActionsFrom(terminal));
        }
    }

    [Fact]
    public void Every_status_is_reachable_from_Submitted()
    {
        var seen = new HashSet<ClaimStatus> { ClaimStatus.Submitted };
        var frontier = new Queue<ClaimStatus>(seen);
        while (frontier.Count > 0)
        {
            var s = frontier.Dequeue();
            foreach (var kv in ClaimStateMachine.Transitions.Where(t => t.Key.From == s))
                if (seen.Add(kv.Value)) frontier.Enqueue(kv.Value);
        }
        Assert.Equal(Enum.GetValues<ClaimStatus>().Length, seen.Count);   // no orphaned status in the table
    }
}

public class ClaimSubmissionTests
{
    [Fact]
    public void Submitting_creates_a_Submitted_claim_with_initial_reserve_and_event()
    {
        var c = Fx.NewClaim(claimed: 5_000m);

        Assert.Equal(ClaimStatus.Submitted, c.Status);
        Assert.Equal(1, c.Version);
        Assert.Equal(4_000m, c.Reserve);   // 5,000 claimed − 1,000 excess
        var evt = Assert.Single(c.DequeueEvents());
        var submitted = Assert.IsType<ClaimSubmitted>(evt);
        Assert.Equal(ClaimStatus.Submitted, submitted.Claim.Status);
        Assert.Equal(1, submitted.Claim.Version);
        Assert.Empty(c.DequeueEvents());   // queue drains
    }

    [Fact]
    public void Initial_reserve_is_capped_by_the_sum_insured()
    {
        Assert.Equal(99_000m, Fx.NewClaim(claimed: 500_000m).Reserve);   // 100,000 limit − 1,000 excess
    }

    [Fact]
    public void Priority_rules_put_people_first_then_money()
    {
        Assert.Equal(ClaimPriority.Normal, Fx.NewClaim(5_000m).Priority);
        Assert.Equal(ClaimPriority.High, Fx.NewClaim(40_000m).Priority);                // ≈ USD 29.6k ≥ 25k
        Assert.Equal(ClaimPriority.Urgent, Fx.NewClaim(2_000m, injury: true).Priority); // injury beats amount
        Assert.Equal(ClaimPriority.Urgent, Claim.PriorityFor(Market.SG, 1_000m, null, new PropertyDetails("x", "Fire", Habitable: false)));
    }

    [Fact]
    public void Only_claimants_can_report_an_incident()
    {
        var data = new SubmitClaimData("P", ClaimType.Motor, new DateOnly(2026, 9, 28), "x", "A description that is long enough.", 100m,
            new MotorDetails(null, false, false, null, true), null);
        Assert.Throws<ForbiddenException>(() => Claim.Submit(Fx.Officer(), data, Market.SG, Fx.Terms(), "N", Fx.T0));
    }

    [Theory]
    [InlineData("short", 100, 9, 28)]          // description too short
    [InlineData("A description that is long enough.", 0, 9, 28)]   // zero amount
    public void Invalid_input_is_a_validation_error(string description, int amount, int month, int day)
    {
        var data = new SubmitClaimData("P", ClaimType.Motor, new DateOnly(2026, month, day), "somewhere", description, amount,
            new MotorDetails(null, false, false, null, true), null);
        var ex = Assert.Throws<BadRequestException>(() => Claim.Submit(Fx.Claimant(), data, Market.SG, Fx.Terms(), "N", Fx.T0));
        Assert.Equal("VALIDATION_ERROR", ex.Code);
    }

    [Fact]
    public void Incident_cannot_be_in_the_future()
    {
        var data = new SubmitClaimData("P", ClaimType.Motor, new DateOnly(2026, 10, 20), "somewhere", "A description that is long enough.", 100m,
            new MotorDetails(null, false, false, null, true), null);
        var ex = Assert.Throws<RuleViolationException>(() => Claim.Submit(Fx.Claimant(), data, Market.SG, Fx.Terms(), "N", Fx.T0));
        Assert.Equal("INCIDENT_IN_FUTURE", ex.Code);
    }
}

public class ClaimWorkflowTests
{
    [Fact]
    public void Happy_path_from_pickup_to_settlement_releases_the_exposure()
    {
        var officer = Fx.Officer();
        var c = Fx.NewClaim(8_000m);
        c.DequeueEvents();

        c.PickUp(officer, Fx.T0.AddHours(1));
        c.Assess(officer, 8_000m, 100m, "verified", Fx.T0.AddHours(2));
        Assert.Equal(7_000m, c.Reserve);                 // payable = 8,000 − 1,000 excess; reserve follows the assessment
        c.Approve(officer, null, Fx.T0.AddHours(3));
        Assert.Equal(ClaimStatus.Approved, c.Status);
        Assert.Equal(7_000m, c.Reserve);                 // still owed until paid
        c.Settle(officer, "PAY-1", Fx.T0.AddHours(4));

        Assert.Equal(ClaimStatus.Settled, c.Status);
        Assert.Equal(0m, c.Reserve);
        Assert.Equal(7_000m, c.PaidAmount);
        Assert.Equal(5, c.Timeline.Count);               // submitted, picked up, assessed, approved, settled

        var events = c.DequeueEvents();
        Assert.Equal(new[] { typeof(ClaimAssigned), typeof(ClaimAssessed), typeof(ClaimApproved), typeof(ClaimSettled) },
            events.Select(e => e.GetType()).ToArray());
        var versions = events.Cast<ClaimEvent>().Select(e => e.Claim.Version).ToArray();
        Assert.True(versions.SequenceEqual(versions.OrderBy(v => v)));   // strictly increasing: consumers can drop stale events
        Assert.Equal(versions.Length, versions.Distinct().Count());
    }

    [Fact]
    public void A_second_officer_cannot_pick_up_the_same_claim()
    {
        var c = Fx.NewClaim();
        c.PickUp(Fx.Officer("o-001"), Fx.T0.AddHours(1));
        var ex = Assert.Throws<ConflictException>(() => c.PickUp(Fx.Officer("o-002"), Fx.T0.AddHours(2)));
        Assert.Equal("ALREADY_ASSIGNED", ex.Code);
    }

    [Fact]
    public void Only_the_assigned_officer_or_a_manager_can_work_a_claim()
    {
        var c = Fx.NewClaim();
        c.PickUp(Fx.Officer("o-001"), Fx.T0.AddHours(1));

        var ex = Assert.Throws<ForbiddenException>(() => c.Assess(Fx.Officer("o-002"), 1_000m, 100m, null, Fx.T0.AddHours(2)));
        Assert.Equal("NOT_ASSIGNED_TO_YOU", ex.Code);

        c.Assess(Fx.Manager(), 1_000m, 100m, null, Fx.T0.AddHours(2));   // manager override
        Assert.Equal(ClaimStatus.Assessed, c.Status);
    }

    [Fact]
    public void Cannot_assess_before_pickup_or_in_the_wrong_state()
    {
        var c = Fx.NewClaim();
        Assert.Throws<ForbiddenException>(() => c.Assess(Fx.Officer(), 1_000m, 100m, null, Fx.T0));   // unassigned
        c.PickUp(Fx.Officer(), Fx.T0);
        c.RequestInformation(Fx.Officer(), "Please send the repair quotation.", Fx.T0);
        var ex = Assert.Throws<ConflictException>(() => c.Assess(Fx.Officer(), 1_000m, 100m, null, Fx.T0));
        Assert.Equal("INVALID_STATE", ex.Code);
    }

    [Fact]
    public void Information_request_loop_pauses_and_resumes_the_claim()
    {
        var officer = Fx.Officer();
        var c = Fx.NewClaim();
        c.PickUp(officer, Fx.T0.AddHours(1));
        c.RequestInformation(officer, "Please send the repair quotation.", Fx.T0.AddHours(2));

        Assert.Equal(ClaimStatus.AwaitingInformation, c.Status);
        Assert.Equal(Fx.T0.AddHours(2), c.AwaitingInfoSince);
        var request = c.OpenInformationRequest!;

        // somebody else's claimant account cannot answer
        Assert.Throws<ForbiddenException>(() => c.ProvideInformation(Fx.Claimant("c-hk-001"), request.Id, "Here it is", Fx.T0.AddHours(3)));

        c.ProvideInformation(Fx.Claimant(), request.Id, "Quotation attached, total 3,200.", Fx.T0.AddHours(4));
        Assert.Equal(ClaimStatus.UnderReview, c.Status);
        Assert.Null(c.AwaitingInfoSince);
        Assert.Null(c.OpenInformationRequest);
        Assert.Equal("Quotation attached, total 3,200.", c.InformationRequests[0].Response);
    }

    [Fact]
    public void Rejecting_needs_a_reason_and_clears_the_reserve()
    {
        var officer = Fx.Officer();
        var c = Fx.NewClaim();
        c.PickUp(officer, Fx.T0);
        Assert.Throws<BadRequestException>(() => c.Reject(officer, "no", Fx.T0));
        c.Reject(officer, "Damage is excluded wear and tear under section 4.", Fx.T0.AddHours(1));
        Assert.Equal(ClaimStatus.Rejected, c.Status);
        Assert.Equal(0m, c.Reserve);
        Assert.False(c.Decision!.Approved);
    }

    [Fact]
    public void Claimants_can_withdraw_until_a_decision_is_made()
    {
        var c = Fx.NewClaim();
        c.Withdraw(Fx.Claimant(), "Sorted it out myself", Fx.T0.AddHours(1));
        Assert.Equal(ClaimStatus.Withdrawn, c.Status);
        Assert.Equal(0m, c.Reserve);

        var approved = Fx.Assessed(Fx.Officer(), 5_000m);
        approved.Approve(Fx.Officer(), null, Fx.T0.AddHours(3));
        Assert.Throws<ConflictException>(() => approved.Withdraw(Fx.Claimant(), null, Fx.T0.AddHours(4)));
        Assert.Throws<ForbiddenException>(() => Fx.NewClaim().Withdraw(Fx.Claimant("c-hk-001"), null, Fx.T0));
    }

    [Fact]
    public void Internal_notes_are_staff_only_and_do_not_raise_events()
    {
        var c = Fx.NewClaim();
        c.DequeueEvents();
        c.AddNote(Fx.Officer(), "Spoke to the workshop.", Fx.T0.AddHours(1));
        Assert.Single(c.Notes);
        Assert.Empty(c.DequeueEvents());
        Assert.Throws<ForbiddenException>(() => c.AddNote(Fx.Claimant(), "hello", Fx.T0));
    }
}

public class ApprovalAuthorityTests
{
    [Theory]
    [InlineData(10_000, 100, 1_000, 100_000, 9_000)]       // loss × 100% − excess
    [InlineData(10_000, 80, 1_000, 100_000, 7_000)]        // partial cover
    [InlineData(500, 100, 1_000, 100_000, 0)]              // loss below the excess pays nothing
    [InlineData(900_000, 100, 1_000, 100_000, 100_000)]    // capped by the sum insured
    public void Payable_amount_formula(int loss, int percent, int excess, int limit, int expected)
    {
        Assert.Equal((decimal)expected, Claim.CalculatePayable(loss, percent, excess, limit));
    }

    [Fact]
    public void An_officer_cannot_approve_above_their_authority_but_a_manager_can()
    {
        var officer = Fx.Officer(authorityUsd: 10_000);   // ≈ SGD 13.5k
        var c = Fx.Assessed(officer, loss: 20_000m);      // payable 19,000 SGD ≈ USD 14k

        Assert.True(c.Assessment!.RequiresManagerApproval);
        var ex = Assert.Throws<RuleViolationException>(() => c.Approve(officer, null, Fx.T0.AddHours(3)));
        Assert.Equal("AUTHORITY_LIMIT", ex.Code);
        Assert.DoesNotContain("Approve", c.AllowedActions(officer));
        Assert.True(c.ActionHints(officer).ContainsKey("Approve"));   // the UI can explain WHY it is blocked

        Assert.Contains("Approve", c.AllowedActions(Fx.Manager()));
        c.Approve(Fx.Manager(), "ok", Fx.T0.AddHours(3));
        Assert.Equal(ClaimStatus.Approved, c.Status);
    }

    [Fact]
    public void An_officer_can_approve_within_their_authority()
    {
        var officer = Fx.Officer(authorityUsd: 10_000);
        var c = Fx.Assessed(officer, loss: 8_000m);       // payable 7,000 SGD ≈ USD 5.2k
        Assert.False(c.Assessment!.RequiresManagerApproval);
        Assert.Contains("Approve", c.AllowedActions(officer));
    }

    [Fact]
    public void Allowed_actions_follow_role_state_and_ownership()
    {
        var c = Fx.NewClaim();
        Assert.Equal(new[] { "Withdraw" }, c.AllowedActions(Fx.Claimant()).ToArray());
        Assert.Contains("PickUp", c.AllowedActions(Fx.Officer()));
        Assert.Contains("Assign", c.AllowedActions(Fx.Manager()));
        Assert.Empty(c.AllowedActions(Fx.Claimant("c-hk-001")));   // someone else's claim
    }
}

public class ClaimViewTests
{
    [Fact]
    public void Claimants_never_receive_internal_notes_or_assessor_remarks()
    {
        var officer = Fx.Officer();
        var c = Fx.Assessed(officer, 5_000m);
        c.AddNote(officer, "Suspect inflated quote — check the workshop.", Fx.T0.AddHours(3));

        var staffView = ClaimDetailDto.For(c, officer, Fx.T0.AddDays(1));
        var claimantView = ClaimDetailDto.For(c, Fx.Claimant(), Fx.T0.AddDays(1));

        Assert.Single(staffView.Notes!);
        Assert.Equal("ok", staffView.Assessment!.Notes);
        Assert.Null(claimantView.Notes);
        Assert.Null(claimantView.Assessment!.Notes);
    }

    [Fact]
    public void Sla_state_moves_from_on_track_to_due_soon_to_breached()
    {
        var urgent = Fx.NewClaim(2_000m, injury: true);   // first-touch window = 2h
        var snap = urgent.Snapshot();
        Assert.Equal(SlaState.OnTrack, Sla.Evaluate(snap, Fx.T0.AddMinutes(30)).State);
        Assert.Equal(SlaState.DueSoon, Sla.Evaluate(snap, Fx.T0.AddMinutes(100)).State);
        Assert.Equal(SlaState.Breached, Sla.Evaluate(snap, Fx.T0.AddHours(3)).State);

        urgent.Withdraw(Fx.Claimant(), null, Fx.T0.AddHours(1));
        Assert.Equal(SlaState.None, Sla.Evaluate(urgent.Snapshot(), Fx.T0.AddHours(9)).State);   // terminal claims have no SLA
    }
}

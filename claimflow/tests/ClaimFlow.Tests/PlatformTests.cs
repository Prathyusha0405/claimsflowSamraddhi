using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using ClaimFlow.Contracts;
using ClaimFlow.Platform.Hosting;
using ClaimFlow.Platform.Messaging;
using ClaimFlow.Platform.Persistence;
using ClaimFlow.Platform.Security;
using ClaimFlow.Reporting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClaimFlow.Tests;

public sealed class TestDoc : IDocument
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Group { get; set; }
}

internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal static class Snap
{
    public static ClaimSnapshot Make(string id = "c1", int version = 1, ClaimStatus status = ClaimStatus.Submitted, Market market = Market.SG,
        decimal reserve = 1_000m, decimal paid = 0m, string? officer = null, DateTimeOffset? submitted = null, DateTimeOffset? decided = null,
        DateTimeOffset? settled = null, ClaimPriority priority = ClaimPriority.Normal) =>
        new(id, "CLM-" + id, version, "POL", "c-1", "Mei", market, ClaimType.Motor, status, priority, Markets.CurrencyOf(market),
            reserve, reserve, null, paid, officer, officer, submitted ?? Fx.T0, officer is null ? null : (submitted ?? Fx.T0).AddHours(1),
            null, decided, settled, Fx.T0);
}

public class DocumentStoreTests
{
    [Fact]
    public void Callers_receive_copies_so_mutating_a_loaded_document_does_not_change_the_store()
    {
        var col = JsonDataStore.InMemory().Collection<TestDoc>("docs");
        col.Upsert(new TestDoc { Id = "1", Name = "original" });

        var loaded = col.Get("1")!;
        loaded.Name = "mutated";

        Assert.Equal("original", col.Get("1")!.Name);
    }

    [Fact]
    public void A_failed_transaction_rolls_everything_back()
    {
        var store = JsonDataStore.InMemory();
        var col = store.Collection<TestDoc>("docs");
        col.Upsert(new TestDoc { Id = "1", Name = "a" });

        Assert.Throws<InvalidOperationException>(() => store.Transaction(() =>
        {
            col.Upsert(new TestDoc { Id = "1", Name = "b" });
            col.Upsert(new TestDoc { Id = "2", Name = "c" });
            col.Delete("1");
            throw new InvalidOperationException("boom");
        }));

        Assert.Equal("a", col.Get("1")!.Name);
        Assert.Null(col.Get("2"));
    }

    [Fact]
    public void Committed_data_survives_a_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"claimflow-test-{Guid.NewGuid():N}.json");
        try
        {
            var first = new JsonDataStore(path);
            first.Collection<TestDoc>("docs").Upsert(new TestDoc { Id = "1", Name = "kept", Group = "g" });
            first.NextSequence("seq");

            var second = new JsonDataStore(path);
            Assert.Equal("kept", second.Collection<TestDoc>("docs").Get("1")!.Name);
            Assert.Equal(2, second.NextSequence("seq"));   // counters persist too
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Secondary_indexes_follow_updates_and_deletes()
    {
        var col = JsonDataStore.InMemory().Collection<TestDoc>("docs").AddIndex("group", d => d.Group);
        col.Upsert(new TestDoc { Id = "1", Group = "a" });
        col.Upsert(new TestDoc { Id = "2", Group = "a" });
        col.Upsert(new TestDoc { Id = "3", Group = "b" });
        Assert.Equal(2, col.ByIndex("group", "a").Count);

        col.Upsert(new TestDoc { Id = "2", Group = "b" });   // moves between index buckets
        Assert.Equal(1, col.ByIndex("group", "a").Count);
        Assert.Equal(2, col.ByIndex("group", "b").Count);

        col.Delete("3");
        Assert.Equal(1, col.ByIndex("group", "b").Count);
        Assert.Empty(col.ByIndex("group", "zzz"));
    }

    [Fact]
    public void After_commit_hooks_run_only_when_the_transaction_commits()
    {
        var store = JsonDataStore.InMemory();
        var ran = 0;

        store.Transaction(() => store.AfterCommit(() => ran++));
        Assert.Equal(1, ran);

        Assert.Throws<InvalidOperationException>(() => store.Transaction(() =>
        {
            store.AfterCommit(() => ran++);
            throw new InvalidOperationException();
        }));
        Assert.Equal(1, ran);
    }
}

public class JwtTests
{
    private static JwtTokenService Service(string secret = "unit-test-secret-unit-test-secret-1234") =>
        new(Options.Create(new JwtOptions { Secret = secret }));

    private static readonly AuthUser Officer = new("o-001", "arjun@x", "Arjun Nair", Roles.ClaimsOfficer, null, 10_000);

    [Fact]
    public void A_valid_token_round_trips_into_a_principal()
    {
        var svc = Service();
        var (token, _) = svc.Issue(Officer, Fx.T0);
        var user = svc.Validate(token, Fx.T0.AddMinutes(5))!.ToCurrentUser();

        Assert.Equal("o-001", user.Id);
        Assert.Equal(Roles.ClaimsOfficer, user.Role);
        Assert.Equal(10_000m, user.AuthorityLimitUsd);
        Assert.True(user.IsStaff);
    }

    [Fact]
    public void Expired_tokens_are_rejected()
    {
        var svc = Service();
        var (token, expires) = svc.Issue(Officer, Fx.T0);
        Assert.Null(svc.Validate(token, expires.AddSeconds(1)));
    }

    [Fact]
    public void A_token_signed_with_another_secret_is_rejected()
    {
        var (token, _) = Service("another-secret-another-secret-123456").Issue(Officer, Fx.T0);
        Assert.Null(Service().Validate(token, Fx.T0.AddMinutes(1)));
    }

    [Fact]
    public void Tampering_with_the_payload_invalidates_the_signature()
    {
        var svc = Service();
        var (token, _) = svc.Issue(Officer, Fx.T0);
        var parts = token.Split('.');
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(parts[1].Replace('-', '+').Replace('_', '/').PadRight((parts[1].Length + 3) / 4 * 4, '=')));
        var forged = json.Replace(Roles.ClaimsOfficer, Roles.Manager);
        var forgedPart = Convert.ToBase64String(Encoding.UTF8.GetBytes(forged)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Assert.Null(svc.Validate($"{parts[0]}.{forgedPart}.{parts[2]}", Fx.T0.AddMinutes(1)));
        Assert.Null(svc.Validate("not.a.jwt", Fx.T0));
        Assert.Null(svc.Validate("garbage", Fx.T0));
    }
}

public class OutboxTests
{
    private sealed class FakeTransport : IMessageTransport
    {
        public string Name => "fake";
        public IReadOnlyList<string> Targets { get; } = ["a", "b"];
        public ConcurrentQueue<(string Target, string Id)> Delivered { get; } = new();
        public volatile bool BIsDown = true;

        public Task DeliverAsync(OutboxMessage message, string target, CancellationToken ct)
        {
            if (target == "b" && BIsDown) throw new InvalidOperationException("b is down");
            Delivered.Enqueue((target, message.Id));
            return Task.CompletedTask;
        }
    }

    private static (JsonDataStore store, DocumentCollection<OutboxMessage> box, OutboxWriter writer, OutboxSignal signal) Build()
    {
        var store = JsonDataStore.InMemory();
        var box = store.Collection<OutboxMessage>("outbox");
        var signal = new OutboxSignal();
        return (store, box, new OutboxWriter(store, box, signal, new ServiceInfo("test", Fx.T0)), signal);
    }

    private static ClaimSubmitted Event(string claimId) => new() { Claim = Snap.Make(claimId) };

    private static async Task WaitFor(Func<bool> condition, int timeoutMs = 8000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < until) await Task.Delay(25);
    }

    [Fact]
    public void An_event_is_recorded_only_if_the_business_transaction_commits()
    {
        var (store, box, writer, _) = Build();
        var docs = store.Collection<TestDoc>("docs");

        Assert.Throws<InvalidOperationException>(() => store.Transaction(() =>
        {
            docs.Upsert(new TestDoc { Id = "1" });
            writer.Enqueue(Event("c1"), "c1");
            throw new InvalidOperationException("business rule failed after enqueue");
        }));
        Assert.Equal(0, box.Count());
        Assert.Equal(0, docs.Count());

        store.Transaction(() => { docs.Upsert(new TestDoc { Id = "1" }); writer.Enqueue(Event("c1"), "c1"); });
        Assert.Equal(1, box.Count());
        Assert.Equal(1, docs.Count());
    }

    [Fact]
    public void Outbox_ids_preserve_publish_order()
    {
        var (store, box, writer, _) = Build();
        store.Transaction(() => { foreach (var i in Enumerable.Range(0, 12)) writer.Enqueue(Event("c" + i), "c" + i); });
        var ids = box.Query().Select(m => m.Id).ToList();
        Assert.True(ids.SequenceEqual(ids.OrderBy(x => x, StringComparer.Ordinal)));
        Assert.Equal(12, ids.Distinct().Count());
    }

    [Fact]
    public async Task One_slow_subscriber_does_not_block_the_others_and_nothing_is_lost_or_reordered()
    {
        var (store, box, writer, signal) = Build();
        var transport = new FakeTransport();
        store.Transaction(() => { foreach (var i in Enumerable.Range(1, 3)) writer.Enqueue(Event("c" + i), "c" + i); });

        var dispatcher = new OutboxDispatcher(store, box, transport, signal, new MessagingStats(), NullLogger<OutboxDispatcher>.Instance);
        await dispatcher.StartAsync(CancellationToken.None);
        try
        {
            await WaitFor(() => transport.Delivered.Count(d => d.Target == "a") == 3);
            Assert.Equal(3, transport.Delivered.Count(d => d.Target == "a"));
            Assert.Equal(0, transport.Delivered.Count(d => d.Target == "b"));
            Assert.Equal(3, box.Count());                       // still held for b — durable until everyone has it

            transport.BIsDown = false;                           // b recovers
            await WaitFor(() => box.Count() == 0);
            Assert.Equal(0, box.Count());

            var forB = transport.Delivered.Where(d => d.Target == "b").Select(d => d.Id).ToList();
            Assert.Equal(3, forB.Count);
            Assert.True(forB.SequenceEqual(forB.OrderBy(x => x, StringComparer.Ordinal)));   // in order
        }
        finally { await dispatcher.StopAsync(CancellationToken.None); }
    }
}

public class DispatcherTests
{
    private sealed class CountingHandler : IEventHandler<ClaimSubmitted>
    {
        public static int Calls;
        public Task HandleAsync(ClaimSubmitted evt, CancellationToken ct) { Interlocked.Increment(ref Calls); return Task.CompletedTask; }
    }

    private static EventEnvelope Envelope(string id, string type, IntegrationEvent payload) =>
        new(id, type, "k", Fx.T0, null, "test", JsonSerializer.SerializeToElement(payload, payload.GetType(), JsonDefaults.Options));

    [Fact]
    public async Task Duplicates_are_skipped_and_unknown_event_types_are_ignored()
    {
        CountingHandler.Calls = 0;
        var provider = new ServiceCollection().AddLogging().AddSingleton<MessagingStats>().AddSingleton<EventDispatcher>()
            .AddEventHandler<ClaimSubmitted, CountingHandler>().BuildServiceProvider();
        var dispatcher = provider.GetRequiredService<EventDispatcher>();
        var evt = new ClaimSubmitted { Claim = Snap.Make() };

        await dispatcher.DispatchAsync(Envelope("e-1", "claim.submitted", evt), CancellationToken.None);
        await dispatcher.DispatchAsync(Envelope("e-1", "claim.submitted", evt), CancellationToken.None);   // redelivery (at-least-once)
        await dispatcher.DispatchAsync(Envelope("e-2", "claim.something-new", evt), CancellationToken.None);   // not subscribed

        Assert.Equal(1, CountingHandler.Calls);
        Assert.Equal(1, provider.GetRequiredService<MessagingStats>().Ignored);
    }

    [Fact]
    public void Event_names_come_from_the_attribute()
    {
        Assert.Equal("claim.submitted", EventNames.Of<ClaimSubmitted>());
        Assert.Equal("claim.information-requested", EventNames.Of<InformationRequested>());
    }
}

public class ReportingTests
{
    private static (DocumentCollection<ClaimView> views, ProjectionHandler<ClaimAssigned> handler, ReportingQueries queries) Build(DateTimeOffset now)
    {
        var store = JsonDataStore.InMemory();
        var views = store.Collection<ClaimView>("v");
        var recent = store.Collection<RecentEvent>("r");
        var clock = new FixedClock(now);
        return (views, new ProjectionHandler<ClaimAssigned>(views, recent, store, clock), new ReportingQueries(views, recent, clock));
    }

    private static ClaimAssigned Assigned(ClaimSnapshot s) => new() { Claim = s, OfficerId = "o-1", OfficerName = "Arjun" };

    [Fact]
    public async Task Stale_and_duplicate_events_never_overwrite_newer_state()
    {
        var (views, handler, _) = Build(Fx.T0);

        await handler.HandleAsync(Assigned(Snap.Make(version: 3, status: ClaimStatus.Assessed)), CancellationToken.None);
        await handler.HandleAsync(Assigned(Snap.Make(version: 2, status: ClaimStatus.UnderReview)), CancellationToken.None);   // late / reordered
        await handler.HandleAsync(Assigned(Snap.Make(version: 3, status: ClaimStatus.Assessed)), CancellationToken.None);      // duplicate

        var view = views.Get("c1")!;
        Assert.Equal(3, view.Snapshot.Version);
        Assert.Equal(ClaimStatus.Assessed, view.Snapshot.Status);
    }

    [Fact]
    public async Task Exposure_counts_only_open_claims_and_normalises_currencies_to_usd()
    {
        var (_, handler, queries) = Build(Fx.T0.AddHours(2));
        // SGD 10,000 open (≈ USD 7,400) · HKD 100,000 open (≈ USD 12,800) · settled and rejected claims carry no exposure
        await handler.HandleAsync(Assigned(Snap.Make("a", market: Market.SG, reserve: 10_000m, status: ClaimStatus.UnderReview, officer: "o-1")), CancellationToken.None);
        await handler.HandleAsync(Assigned(Snap.Make("b", market: Market.HK, reserve: 100_000m, status: ClaimStatus.Approved, officer: "o-1")), CancellationToken.None);
        await handler.HandleAsync(Assigned(Snap.Make("c", market: Market.SG, reserve: 0m, status: ClaimStatus.Settled, paid: 5_000m, officer: "o-1")), CancellationToken.None);
        await handler.HandleAsync(Assigned(Snap.Make("d", market: Market.SG, reserve: 0m, status: ClaimStatus.Rejected, officer: "o-1")), CancellationToken.None);

        var report = queries.Exposure();
        Assert.Equal(20_200m, report.TotalUsd);                              // 7,400 + 12,800
        Assert.Equal(2, report.ByMarket.Count);
        Assert.Equal("Hong Kong", report.ByMarket[0].Label);                 // biggest exposure first
        Assert.Equal(100_000m, report.ByMarket[0].Reserve);
        Assert.Equal("HKD", report.ByMarket[0].Currency);
    }

    [Fact]
    public async Task Overview_and_sla_breaches_reflect_the_projection()
    {
        var now = Fx.T0.AddHours(30);
        var (_, handler, queries) = Build(now);
        // unassigned for 30h with a 24h target → breach;  unassigned for 30h but High priority (4h) → breach;  fresh claim → fine
        await handler.HandleAsync(Assigned(Snap.Make("late", status: ClaimStatus.Submitted, submitted: Fx.T0)), CancellationToken.None);
        await handler.HandleAsync(Assigned(Snap.Make("urgent", status: ClaimStatus.Submitted, submitted: Fx.T0.AddHours(25), priority: ClaimPriority.High)), CancellationToken.None);
        await handler.HandleAsync(Assigned(Snap.Make("fresh", status: ClaimStatus.Submitted, submitted: now.AddHours(-1))), CancellationToken.None);

        var overview = queries.Overview();
        Assert.Equal(3, overview.OpenClaims);
        Assert.Equal(3, overview.Unassigned);
        Assert.Equal(2, overview.SlaBreaches);
        Assert.Equal(new[] { "CLM-late", "CLM-urgent" }, queries.SlaBreaches().Select(b => b.ClaimNumber).ToArray());
    }

    [Fact]
    public async Task Workload_groups_open_claims_by_officer_and_reports_the_approval_rate()
    {
        var (_, handler, queries) = Build(Fx.T0.AddDays(2));
        await handler.HandleAsync(Assigned(Snap.Make("1", status: ClaimStatus.UnderReview, officer: "o-1")), CancellationToken.None);
        await handler.HandleAsync(Assigned(Snap.Make("2", status: ClaimStatus.AwaitingInformation, officer: "o-1")), CancellationToken.None);
        await handler.HandleAsync(Assigned(Snap.Make("3", status: ClaimStatus.Settled, officer: "o-1", paid: 100m, reserve: 0m, decided: Fx.T0.AddDays(1), settled: Fx.T0.AddDays(1))), CancellationToken.None);
        await handler.HandleAsync(Assigned(Snap.Make("4", status: ClaimStatus.Rejected, officer: "o-1", reserve: 0m, decided: Fx.T0.AddDays(1))), CancellationToken.None);

        var row = Assert.Single(queries.Workload());
        Assert.Equal(2, row.Open);
        Assert.Equal(1, row.UnderReview);
        Assert.Equal(1, row.AwaitingInformation);
        Assert.Equal(0.5, row.ApprovalRate);   // 1 approved/settled of 2 decided
    }
}

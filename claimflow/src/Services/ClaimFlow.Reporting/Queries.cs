using ClaimFlow.Contracts;
using ClaimFlow.Platform.Persistence;

namespace ClaimFlow.Reporting;

public sealed record Overview(
    DateTimeOffset AsOf, int TotalClaims, int OpenClaims, int Unassigned, int AwaitingInformation, int AwaitingApproval,
    int ApprovedPendingPayment, decimal ExposureUsd, int SlaBreaches, double? AvgHoursToFirstAssignment,
    double? AvgDaysToDecision, double? ApprovalRate, decimal PaidLast30dUsd, int DecidedLast30d);

public sealed record ExposureRow(string Key, string Label, string? Currency, int Count, decimal Reserve, decimal ReserveUsd);
public sealed record ExposureReport(decimal TotalUsd, IReadOnlyList<ExposureRow> ByMarket, IReadOnlyList<ExposureRow> ByType, IReadOnlyList<ExposureRow> ByStatus);
public sealed record StatusCount(ClaimStatus Status, int Count);
public sealed record OfficerWorkload(
    string OfficerId, string OfficerName, int Open, int UnderReview, int AwaitingInformation, int Assessed, int ApprovedPendingPayment,
    decimal ExposureUsd, int SlaBreaches, double? OldestOpenHours, int DecidedLast30d, double? ApprovalRate, double? AvgDaysToDecision);
public sealed record TrendPoint(DateOnly Date, int Submitted, int Decided, int Settled, decimal PaidUsd);
public sealed record SlaBreach(string ClaimId, string ClaimNumber, ClaimStatus Status, ClaimPriority Priority, Market Market,
    string? OfficerName, string Stage, DateTimeOffset DueAt, double OverdueHours, decimal ReserveUsd);

/// <summary>
/// Read-side queries over the projection. At this scale they scan the (small) view collection on demand; at real scale you
/// would maintain pre-aggregated counters per event or push the projection into a store built for analytics.
/// </summary>
public sealed class ReportingQueries(DocumentCollection<ClaimView> views, DocumentCollection<RecentEvent> recent, TimeProvider clock)
{
    private DateTimeOffset Now => clock.GetUtcNow();
    private IReadOnlyList<ClaimSnapshot> All() => views.Query().Select(v => v.Snapshot).ToList();
    private static bool IsOpen(ClaimSnapshot s) => s.Status is not (ClaimStatus.Rejected or ClaimStatus.Settled or ClaimStatus.Withdrawn);
    private static bool WasDecided(ClaimSnapshot s) => s.DecidedAt is not null && s.Status is ClaimStatus.Approved or ClaimStatus.Settled or ClaimStatus.Rejected;
    private static decimal Usd(ClaimSnapshot s) => Markets.ToUsd(s.Market, s.Reserve);

    public Overview Overview()
    {
        var all = All();
        var now = Now;
        var open = all.Where(IsOpen).ToList();
        var decided = all.Where(WasDecided).ToList();
        var recentDecided = decided.Where(s => s.DecidedAt >= now.AddDays(-30)).ToList();
        var assigned = all.Where(s => s.FirstAssignedAt is not null).ToList();

        return new Overview(
            now, all.Count, open.Count,
            open.Count(s => s.Status == ClaimStatus.Submitted),
            open.Count(s => s.Status == ClaimStatus.AwaitingInformation),
            open.Count(s => s.Status == ClaimStatus.Assessed),
            open.Count(s => s.Status == ClaimStatus.Approved),
            Math.Round(open.Sum(Usd), 0),
            open.Count(s => Sla.Evaluate(s, now).State == SlaState.Breached),
            assigned.Count == 0 ? null : Math.Round(assigned.Average(s => (s.FirstAssignedAt!.Value - s.SubmittedAt).TotalHours), 1),
            decided.Count == 0 ? null : Math.Round(decided.Average(s => (s.DecidedAt!.Value - s.SubmittedAt).TotalDays), 1),
            decided.Count == 0 ? null : Math.Round(decided.Count(s => s.Status != ClaimStatus.Rejected) / (double)decided.Count, 3),
            Math.Round(all.Where(s => s.SettledAt >= now.AddDays(-30)).Sum(s => Markets.ToUsd(s.Market, s.PaidAmount)), 0),
            recentDecided.Count);
    }

    /// <summary>"What is our outstanding liability?" — reserves of open claims, in local currency and normalised to USD.</summary>
    public ExposureReport Exposure()
    {
        var open = All().Where(IsOpen).ToList();
        ExposureRow[] Group(Func<ClaimSnapshot, string> key, Func<string, string> label, bool withCurrency = false) =>
            open.GroupBy(key).OrderByDescending(g => g.Sum(Usd))
                .Select(g => new ExposureRow(g.Key, label(g.Key), withCurrency ? g.First().Currency : null, g.Count(),
                    withCurrency ? g.Sum(s => s.Reserve) : 0, Math.Round(g.Sum(Usd), 0))).ToArray();

        return new ExposureReport(
            Math.Round(open.Sum(Usd), 0),
            Group(s => s.Market.ToString(), k => Markets.Get(Enum.Parse<Market>(k)).Name, withCurrency: true),
            Group(s => s.Type.ToString(), k => k),
            Group(s => s.Status.ToString(), k => k));
    }

    public IReadOnlyList<StatusCount> StatusBreakdown()
    {
        var all = All();
        return Enum.GetValues<ClaimStatus>().Select(st => new StatusCount(st, all.Count(s => s.Status == st))).ToList();
    }

    public IReadOnlyList<OfficerWorkload> Workload()
    {
        var all = All();
        var now = Now;
        return all.Where(s => s.AssignedOfficerId is not null)
            .GroupBy(s => (Id: s.AssignedOfficerId!, Name: s.AssignedOfficerName ?? s.AssignedOfficerId!))
            .Select(g =>
            {
                var open = g.Where(IsOpen).ToList();
                var decided = g.Where(WasDecided).ToList();
                var decided30 = decided.Where(s => s.DecidedAt >= now.AddDays(-30)).ToList();
                return new OfficerWorkload(
                    g.Key.Id, g.Key.Name, open.Count,
                    open.Count(s => s.Status == ClaimStatus.UnderReview),
                    open.Count(s => s.Status == ClaimStatus.AwaitingInformation),
                    open.Count(s => s.Status == ClaimStatus.Assessed),
                    open.Count(s => s.Status == ClaimStatus.Approved),
                    Math.Round(open.Sum(Usd), 0),
                    open.Count(s => Sla.Evaluate(s, now).State == SlaState.Breached),
                    open.Count == 0 ? null : Math.Round(open.Max(s => (now - s.SubmittedAt).TotalHours), 1),
                    decided30.Count,
                    decided.Count == 0 ? null : Math.Round(decided.Count(s => s.Status != ClaimStatus.Rejected) / (double)decided.Count, 3),
                    decided.Count == 0 ? null : Math.Round(decided.Average(s => (s.DecidedAt!.Value - s.SubmittedAt).TotalDays), 1));
            })
            .OrderByDescending(w => w.Open).ThenBy(w => w.OfficerName).ToList();
    }

    public IReadOnlyList<TrendPoint> Trend(int days)
    {
        days = Math.Clamp(days, 3, 60);
        var all = All();
        var today = DateOnly.FromDateTime(Now.UtcDateTime);
        return Enumerable.Range(0, days).Select(i => today.AddDays(i - (days - 1))).Select(d =>
        {
            bool On(DateTimeOffset? t) => t is { } v && DateOnly.FromDateTime(v.UtcDateTime) == d;
            return new TrendPoint(d,
                all.Count(s => On(s.SubmittedAt)),
                all.Count(s => WasDecided(s) && On(s.DecidedAt)),
                all.Count(s => On(s.SettledAt)),
                Math.Round(all.Where(s => On(s.SettledAt)).Sum(s => Markets.ToUsd(s.Market, s.PaidAmount)), 0));
        }).ToList();
    }

    public IReadOnlyList<SlaBreach> SlaBreaches()
    {
        var now = Now;
        return All().Where(IsOpen)
            .Select(s => (s, sla: Sla.Evaluate(s, now)))
            .Where(x => x.sla.State == SlaState.Breached && x.sla.DueAt is not null)
            .Select(x => new SlaBreach(x.s.ClaimId, x.s.ClaimNumber, x.s.Status, x.s.Priority, x.s.Market, x.s.AssignedOfficerName,
                x.sla.Stage ?? "", x.sla.DueAt!.Value, Math.Round((now - x.sla.DueAt!.Value).TotalHours, 1), Usd(x.s)))
            .OrderByDescending(b => b.OverdueHours).ToList();
    }

    public IReadOnlyList<RecentEvent> RecentEvents(int limit) =>
        recent.Query().OrderByDescending(e => e.Id).Take(Math.Clamp(limit, 1, 100)).ToList();
}

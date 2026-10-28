using ClaimFlow.Claims.Application;
using ClaimFlow.Claims.Domain;
using ClaimFlow.Contracts;
using ClaimFlow.Platform.Security;

namespace ClaimFlow.Claims.Api;

public static class ClaimEndpoints
{
    private static Actor Who(HttpContext ctx) => Actor.From(ctx.User.ToCurrentUser());

    public static IEndpointRouteBuilder MapClaimEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/claims").RequireAuthorization();

        // ── claimant ──────────────────────────────────────────────────────────────────────────
        g.MapPost("/", async (SubmitClaimRequest req, HttpContext ctx, ClaimService svc, TimeProvider clock, CancellationToken ct) =>
        {
            var actor = Who(ctx);
            var claim = await svc.SubmitAsync(actor, req, ct);
            return Results.Created($"/claims/{claim.Id}", ClaimDetailDto.For(claim, actor, clock.GetUtcNow()));
        }).RequireAuthorization(Policies.Claimant);

        g.MapGet("/mine", (HttpContext ctx, ClaimService svc, TimeProvider clock) =>
            Results.Ok(svc.Mine(Who(ctx)).Select(c => ClaimSummaryDto.From(c, clock.GetUtcNow()))))
            .RequireAuthorization(Policies.Claimant);

        g.MapPost("/{id}/information-requests/{requestId}/respond",
            (string id, string requestId, InformationResponseBody body, HttpContext ctx, ClaimService svc, TimeProvider clock) =>
                Detail(svc.ProvideInformation(Who(ctx), id, requestId, body.Response), ctx, clock))
            .RequireAuthorization(Policies.Claimant);

        g.MapPost("/{id}/withdraw", (string id, WithdrawBody? body, HttpContext ctx, ClaimService svc, TimeProvider clock) =>
            Detail(svc.Withdraw(Who(ctx), id, body?.Reason), ctx, clock))
            .RequireAuthorization(Policies.Claimant);

        // ── staff: finding work ───────────────────────────────────────────────────────────────
        g.MapGet("/queue", (Market? market, ClaimType? type, ClaimPriority? priority, ClaimService svc, TimeProvider clock) =>
            Results.Ok(svc.Queue(market, type, priority).Select(c => ClaimSummaryDto.From(c, clock.GetUtcNow()))))
            .RequireAuthorization(Policies.Staff);

        g.MapGet("/my-work", (HttpContext ctx, ClaimService svc, TimeProvider clock) =>
            Results.Ok(svc.MyWork(Who(ctx)).Select(c => ClaimSummaryDto.From(c, clock.GetUtcNow()))))
            .RequireAuthorization(Policies.Staff);

        g.MapGet("/", (ClaimStatus? status, string? officerId, Market? market, ClaimType? type, string? q, int? page, int? pageSize,
                ClaimService svc, TimeProvider clock) =>
            {
                var result = svc.Search(status, officerId, market, type, q, page ?? 1, pageSize ?? 25);
                var now = clock.GetUtcNow();
                return Results.Ok(new PagedResult<ClaimSummaryDto>(result.Items.Select(c => ClaimSummaryDto.From(c, now)).ToList(), result.Total, result.Page, result.PageSize));
            })
            .RequireAuthorization(Policies.Staff);

        // ── anyone (own claims for claimants, all for staff) ──────────────────────────────────
        g.MapGet("/{id}", (string id, HttpContext ctx, ClaimService svc, TimeProvider clock) =>
        {
            var actor = Who(ctx);
            return Results.Ok(ClaimDetailDto.For(svc.Get(actor, id), actor, clock.GetUtcNow()));
        });

        // ── staff: working a claim ────────────────────────────────────────────────────────────
        g.MapPost("/{id}/pickup", (string id, HttpContext ctx, ClaimService svc, TimeProvider clock) =>
            Detail(svc.PickUp(Who(ctx), id), ctx, clock));

        g.MapPost("/{id}/assign", (string id, AssignBody body, HttpContext ctx, ClaimService svc, TimeProvider clock) =>
            Detail(svc.Assign(Who(ctx), id, body.OfficerId), ctx, clock));

        g.MapPost("/{id}/request-information", (string id, InformationRequestBody body, HttpContext ctx, ClaimService svc, TimeProvider clock) =>
            Detail(svc.RequestInformation(Who(ctx), id, body.Question), ctx, clock));

        g.MapPost("/{id}/assess", (string id, AssessBody body, HttpContext ctx, ClaimService svc, TimeProvider clock) =>
            Detail(svc.Assess(Who(ctx), id, body.AssessedLoss, body.CoveredPercent, body.Notes), ctx, clock));

        g.MapPost("/{id}/approve", (string id, ApproveBody? body, HttpContext ctx, ClaimService svc, TimeProvider clock) =>
            Detail(svc.Approve(Who(ctx), id, body?.Notes), ctx, clock));

        g.MapPost("/{id}/reject", (string id, RejectBody body, HttpContext ctx, ClaimService svc, TimeProvider clock) =>
            Detail(svc.Reject(Who(ctx), id, body.Reason), ctx, clock));

        g.MapPost("/{id}/settle", (string id, SettleBody? body, HttpContext ctx, ClaimService svc, TimeProvider clock) =>
            Detail(svc.Settle(Who(ctx), id, body?.PaymentReference), ctx, clock));

        g.MapPost("/{id}/notes", (string id, NoteBody body, HttpContext ctx, ClaimService svc, TimeProvider clock) =>
            Detail(svc.AddNote(Who(ctx), id, body.Text), ctx, clock));

        return app;
    }

    private static IResult Detail(Claim claim, HttpContext ctx, TimeProvider clock) =>
        Results.Ok(ClaimDetailDto.For(claim, Who(ctx), clock.GetUtcNow()));
}

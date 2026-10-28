using ClaimFlow.Contracts;
using ClaimFlow.Platform.Hosting;
using ClaimFlow.Platform.Messaging;
using ClaimFlow.Platform.Persistence;
using ClaimFlow.Platform.Security;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.AddClaimFlowDefaults("policy");
builder.Services.AddJsonDataStore(builder.Configuration, "policy");
builder.Services.Configure<MessagingOptions>(builder.Configuration.GetSection("Messaging"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<JsonDataStore>().Collection<PolicyDoc>("policies").AddIndex("claimant", p => p.ClaimantId));
builder.Services.AddSingleton<CoverageService>();

var app = builder.Build();
app.UseClaimFlowDefaults();

// Seed the demo policy book on first run.
var policies = app.Services.GetRequiredService<DocumentCollection<PolicyDoc>>();
if (policies.Count() == 0)
{
    app.Services.GetRequiredService<JsonDataStore>().Transaction(() =>
    {
        foreach (var d in DemoCatalog.BuildPolicies(DateOnly.FromDateTime(DateTime.UtcNow))) policies.Upsert(PolicyDoc.From(d));
    });
    app.Logger.LogInformation("Seeded {Count} demo policies", policies.Count());
}

// GET /policies/mine — a claimant's own policies (drives the policy picker in the claim wizard).
app.MapGet("/policies/mine", (HttpContext ctx, DocumentCollection<PolicyDoc> col) =>
{
    var me = ctx.User.ToCurrentUser();
    return Results.Ok(col.ByIndex("claimant", me.Id).OrderBy(p => p.PolicyNumber).Select(p => PolicyDto.From(p)));
}).RequireAuthorization(Policies.Claimant);

// GET /policies/{number} — owner or staff.
app.MapGet("/policies/{number}", (string number, HttpContext ctx, DocumentCollection<PolicyDoc> col) =>
{
    var me = ctx.User.ToCurrentUser();
    var p = col.Get(number) ?? throw new NotFoundException("POLICY_NOT_FOUND", $"Policy {number} was not found.");
    if (me.IsClaimant && p.ClaimantId != me.Id) throw new ForbiddenException("FORBIDDEN", "You can only view your own policies.");
    return Results.Ok(PolicyDto.From(p));
}).RequireAuthorization();

// GET /policies — staff search.
app.MapGet("/policies", (string? claimantId, DocumentCollection<PolicyDoc> col) =>
    Results.Ok((claimantId is null ? col.Query() : col.ByIndex("claimant", claimantId)).OrderBy(p => p.PolicyNumber).Select(p => PolicyDto.From(p))))
    .RequireAuthorization(Policies.Staff);

// GET /policies/{number}/coverage — SYNCHRONOUS service-to-service check made by Claims at intake.
// Why sync REST: the claimant needs an immediate yes/no, and we must not accept claims on lapsed cover.
app.MapGet("/policies/{number}/coverage", (string number, DateOnly incidentDate, ClaimType type, HttpContext ctx,
    CoverageService coverage, IOptions<MessagingOptions> opts) =>
{
    if (!ctx.HasInternalKey(opts.Value.InternalKey)) return Results.Unauthorized();
    return Results.Ok(coverage.Check(number, incidentDate, type));
});

app.Run();

public sealed class PolicyDoc : IDocument
{
    public string Id { get => PolicyNumber; set => PolicyNumber = value; }
    public string PolicyNumber { get; set; } = "";
    public string ClaimantId { get; set; } = "";
    public string ClaimantName { get; set; } = "";
    public Market Market { get; set; }
    public ClaimType Product { get; set; }
    public string ProductName { get; set; } = "";
    public string Currency { get; set; } = "";
    public decimal CoverageLimit { get; set; }
    public decimal Deductible { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly EffectiveTo { get; set; }
    public string Status { get; set; } = "Active";      // Active | Expired | Cancelled
    public string AssetDescription { get; set; } = "";
    public string? VehicleRegistration { get; set; }
    public string? PropertyAddress { get; set; }

    public static PolicyDoc From(DemoPolicy d) => new()
    {
        PolicyNumber = d.PolicyNumber, ClaimantId = d.ClaimantId, ClaimantName = d.ClaimantName, Market = d.Market,
        Product = d.Product, ProductName = d.ProductName, Currency = d.Currency, CoverageLimit = d.CoverageLimit,
        Deductible = d.Deductible, EffectiveFrom = d.EffectiveFrom, EffectiveTo = d.EffectiveTo, Status = d.Status,
        AssetDescription = d.AssetDescription, VehicleRegistration = d.VehicleRegistration, PropertyAddress = d.PropertyAddress,
    };

    public PolicyTerms ToTerms() => new(ProductName, Currency, CoverageLimit, Deductible, EffectiveFrom, EffectiveTo, AssetDescription);
}

public sealed record PolicyDto(
    string PolicyNumber, string ClaimantId, string ClaimantName, Market Market, ClaimType Product, string ProductName,
    string Currency, decimal CoverageLimit, decimal Deductible, DateOnly EffectiveFrom, DateOnly EffectiveTo,
    string Status, bool InForce, string AssetDescription, string? VehicleRegistration, string? PropertyAddress)
{
    public static PolicyDto From(PolicyDoc p)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var inForce = p.Status == "Active" && today >= p.EffectiveFrom && today <= p.EffectiveTo;
        return new(p.PolicyNumber, p.ClaimantId, p.ClaimantName, p.Market, p.Product, p.ProductName, p.Currency,
            p.CoverageLimit, p.Deductible, p.EffectiveFrom, p.EffectiveTo, p.Status, inForce, p.AssetDescription,
            p.VehicleRegistration, p.PropertyAddress);
    }
}

/// <summary>The business rules for "is this incident covered by this policy?".</summary>
public sealed class CoverageService(DocumentCollection<PolicyDoc> policies)
{
    public CoverageCheckResult Check(string policyNumber, DateOnly incidentDate, ClaimType type)
    {
        var p = policies.Get(policyNumber);
        if (p is null)
            return Fail("POLICY_NOT_FOUND", $"We could not find policy {policyNumber}.");
        if (p.Status != "Active")
            return Fail("POLICY_NOT_ACTIVE", $"Policy {policyNumber} is {p.Status.ToLowerInvariant()} and cannot accept new claims.", p);
        if (incidentDate < p.EffectiveFrom || incidentDate > p.EffectiveTo)
            return Fail("POLICY_OUT_OF_PERIOD", $"The incident date {incidentDate:dd MMM yyyy} is outside the policy period ({p.EffectiveFrom:dd MMM yyyy} – {p.EffectiveTo:dd MMM yyyy}).", p);
        if (p.Product != type)
            return Fail("POLICY_TYPE_MISMATCH", $"Policy {policyNumber} is a {p.Product.ToString().ToLowerInvariant()} policy and does not cover {type.ToString().ToLowerInvariant()} claims.", p);

        return new CoverageCheckResult(true, null, null, p.PolicyNumber, p.ClaimantId, p.ClaimantName, p.Market, p.ToTerms());
    }

    private static CoverageCheckResult Fail(string code, string reason, PolicyDoc? p = null) =>
        new(false, code, reason, p?.PolicyNumber, p?.ClaimantId, p?.ClaimantName, p?.Market, p?.ToTerms());
}

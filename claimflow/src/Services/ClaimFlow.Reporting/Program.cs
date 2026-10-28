using ClaimFlow.Contracts;
using ClaimFlow.Platform.Hosting;
using ClaimFlow.Platform.Messaging;
using ClaimFlow.Platform.Persistence;
using ClaimFlow.Platform.Security;
using ClaimFlow.Reporting;

var builder = WebApplication.CreateBuilder(args);
builder.AddClaimFlowDefaults("reporting");
builder.Services.AddJsonDataStore(builder.Configuration, "reporting");
builder.Services.AddSingleton(sp => sp.GetRequiredService<JsonDataStore>().Collection<ClaimView>("claim-views"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<JsonDataStore>().Collection<RecentEvent>("recent-events"));
builder.Services.AddSingleton<ReportingQueries>();

// Pure SUBSCRIBER: the read model is built only from events — Reporting never calls the Claims service.
builder.Services.AddEventSubscribing(builder.Configuration);
builder.Services.AddEventHandler<ClaimSubmitted, ProjectionHandler<ClaimSubmitted>>();
builder.Services.AddEventHandler<ClaimAssigned, ProjectionHandler<ClaimAssigned>>();
builder.Services.AddEventHandler<InformationRequested, ProjectionHandler<InformationRequested>>();
builder.Services.AddEventHandler<InformationProvided, ProjectionHandler<InformationProvided>>();
builder.Services.AddEventHandler<ClaimAssessed, ProjectionHandler<ClaimAssessed>>();
builder.Services.AddEventHandler<ClaimApproved, ProjectionHandler<ClaimApproved>>();
builder.Services.AddEventHandler<ClaimRejected, ProjectionHandler<ClaimRejected>>();
builder.Services.AddEventHandler<ClaimSettled, ProjectionHandler<ClaimSettled>>();
builder.Services.AddEventHandler<ClaimWithdrawn, ProjectionHandler<ClaimWithdrawn>>();

var app = builder.Build();
app.UseClaimFlowDefaults();
app.MapEventSubscriber();

var g = app.MapGroup("/reports");

// Managers only: financial exposure is sensitive.
g.MapGet("/overview", (ReportingQueries q) => Results.Ok(q.Overview())).RequireAuthorization(Policies.Manager);
g.MapGet("/exposure", (ReportingQueries q) => Results.Ok(q.Exposure())).RequireAuthorization(Policies.Manager);

// Claims staff (officers + managers): workload, performance, SLA, flow.
g.MapGet("/status-breakdown", (ReportingQueries q) => Results.Ok(q.StatusBreakdown())).RequireAuthorization(Policies.Staff);
g.MapGet("/workload", (ReportingQueries q) => Results.Ok(q.Workload())).RequireAuthorization(Policies.Staff);
g.MapGet("/trend", (int? days, ReportingQueries q) => Results.Ok(q.Trend(days ?? 14))).RequireAuthorization(Policies.Staff);
g.MapGet("/sla-breaches", (ReportingQueries q) => Results.Ok(q.SlaBreaches())).RequireAuthorization(Policies.Staff);
g.MapGet("/events/recent", (int? limit, ReportingQueries q) => Results.Ok(q.RecentEvents(limit ?? 30))).RequireAuthorization(Policies.Staff);

app.Run();

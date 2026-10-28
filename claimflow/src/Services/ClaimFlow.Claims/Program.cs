using ClaimFlow.Claims.Api;
using ClaimFlow.Claims.Application;
using ClaimFlow.Claims.Domain;
using ClaimFlow.Claims.Seed;
using ClaimFlow.Platform.Hosting;
using ClaimFlow.Platform.Messaging;
using ClaimFlow.Platform.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.AddClaimFlowDefaults("claims");

// Persistence: the Claims service owns its data (database-per-service). Secondary indexes make the officer's
// workload queries O(matches) instead of scanning every claim.
builder.Services.AddJsonDataStore(builder.Configuration, "claims");
builder.Services.AddSingleton(sp => sp.GetRequiredService<JsonDataStore>().Collection<Claim>("claims")
    .AddIndex("claimant", c => c.ClaimantId)
    .AddIndex("status", c => c.Status.ToString())
    .AddIndex("assignee", c => c.AssignedOfficerId)
    .AddIndex("number", c => c.ClaimNumber));

// Messaging: this service PUBLISHES claim events through the transactional outbox.
// Messaging:Provider decides the wire: "Http" (direct fan-out, no broker) or "Kafka".
builder.Services.AddEventPublishing(builder.Configuration);

// Synchronous dependency on the Policy service (REST) with correlation-id propagation.
builder.Services.AddHttpClient<IPolicyClient, HttpPolicyClient>((sp, http) =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    http.BaseAddress = new Uri(cfg["Services:Policy"] ?? "http://localhost:5102");
    http.Timeout = TimeSpan.FromSeconds(4);
    http.DefaultRequestHeaders.Add("X-Internal-Key", cfg["Messaging:InternalKey"] ?? "claimflow-internal-dev-key");
}).AddHttpMessageHandler<CorrelationIdHandler>();

builder.Services.AddSingleton<StaffDirectory>();
builder.Services.AddSingleton<ClaimService>();
builder.Services.AddSingleton<DemoDataSeeder>();

var app = builder.Build();
app.UseClaimFlowDefaults();
app.MapClaimEndpoints();

if (app.Configuration.GetValue("Seed:Enabled", true))
    app.Services.GetRequiredService<DemoDataSeeder>().SeedIfEmpty();

app.Run();

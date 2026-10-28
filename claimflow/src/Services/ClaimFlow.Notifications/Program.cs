using ClaimFlow.Contracts;
using ClaimFlow.Notifications;
using ClaimFlow.Platform.Hosting;
using ClaimFlow.Platform.Messaging;
using ClaimFlow.Platform.Persistence;
using ClaimFlow.Platform.Security;

var builder = WebApplication.CreateBuilder(args);
builder.AddClaimFlowDefaults("notifications");
builder.Services.AddJsonDataStore(builder.Configuration, "notifications");
builder.Services.AddSingleton(sp => sp.GetRequiredService<JsonDataStore>()
    .Collection<NotificationDoc>("notifications").AddIndex("recipient", n => n.RecipientId));

// This service is a pure SUBSCRIBER: it reacts to claim events and owns nothing the Claims service needs.
builder.Services.AddEventSubscribing(builder.Configuration);
builder.Services.AddEventHandler<ClaimSubmitted, ClaimNotificationHandler>();
builder.Services.AddEventHandler<ClaimAssigned, ClaimNotificationHandler>();
builder.Services.AddEventHandler<InformationRequested, ClaimNotificationHandler>();
builder.Services.AddEventHandler<InformationProvided, ClaimNotificationHandler>();
builder.Services.AddEventHandler<ClaimAssessed, ClaimNotificationHandler>();
builder.Services.AddEventHandler<ClaimApproved, ClaimNotificationHandler>();
builder.Services.AddEventHandler<ClaimRejected, ClaimNotificationHandler>();
builder.Services.AddEventHandler<ClaimSettled, ClaimNotificationHandler>();
builder.Services.AddEventHandler<ClaimWithdrawn, ClaimNotificationHandler>();

var app = builder.Build();
app.UseClaimFlowDefaults();
app.MapEventSubscriber();   // HTTP mode entry point (/internal/events); idle when Kafka is the transport

// The inbox for the signed-in user: personal notifications + role-wide ones (e.g. "approval needed" for managers).
static IEnumerable<NotificationDoc> Mine(CurrentUser me, DocumentCollection<NotificationDoc> col)
{
    var list = col.ByIndex("recipient", me.Id).AsEnumerable();
    if (me.IsManager) list = list.Concat(col.ByIndex("recipient", "role:Manager"));
    return list;
}

app.MapGet("/notifications", (bool? unreadOnly, int? limit, HttpContext ctx, DocumentCollection<NotificationDoc> col) =>
{
    var me = ctx.User.ToCurrentUser();
    var items = Mine(me, col).Where(n => unreadOnly != true || n.ReadAt is null)
        .OrderByDescending(n => n.CreatedAt).Take(Math.Clamp(limit ?? 30, 1, 100)).Select(NotificationDto.From);
    return Results.Ok(items);
}).RequireAuthorization();

app.MapGet("/notifications/unread-count", (HttpContext ctx, DocumentCollection<NotificationDoc> col) =>
    Results.Ok(new { count = Mine(ctx.User.ToCurrentUser(), col).Count(n => n.ReadAt is null) })).RequireAuthorization();

app.MapPost("/notifications/{id}/read", (string id, HttpContext ctx, DocumentCollection<NotificationDoc> col, JsonDataStore store, TimeProvider clock) =>
{
    var me = ctx.User.ToCurrentUser();
    var n = Mine(me, col).FirstOrDefault(x => x.Id == id) ?? throw new NotFoundException("NOTIFICATION_NOT_FOUND", "Notification not found.");
    if (n.ReadAt is null) { n.ReadAt = clock.GetUtcNow(); col.Upsert(n); }
    return Results.Ok(NotificationDto.From(n));
}).RequireAuthorization();

app.MapPost("/notifications/read-all", (HttpContext ctx, DocumentCollection<NotificationDoc> col, JsonDataStore store, TimeProvider clock) =>
{
    var me = ctx.User.ToCurrentUser();
    var now = clock.GetUtcNow();
    var count = 0;
    store.Transaction(() =>
    {
        foreach (var n in Mine(me, col).Where(n => n.ReadAt is null)) { n.ReadAt = now; col.Upsert(n); count++; }
    });
    return Results.Ok(new { marked = count });
}).RequireAuthorization();

app.Run();

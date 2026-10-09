using AppTemplate.Api.Common.Hosting;
using AppTemplate.Api.Common.Observability;
using AppTemplate.Api.Common.Security;
using AppTemplate.Api.Features.Auth.Composition;
using AppTemplate.Api.Features.BackgroundJobs.Jobs;
using AppTemplate.Api.Features.Reminders.Directories;
using AppTemplate.Application;
using AppTemplate.Application.Core;
using AppTemplate.Application.Features.Reminders.Ports.ReminderRecipients;
using AppTemplate.Infrastructure.Core;
using AppTemplate.Infrastructure.Email;
using AppTemplate.Infrastructure.Persistence;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Storage;
using AppTemplate.Presentation.Core.Common.Outbound;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Clears the console provider the host adds by default, so the JSON one below is the only sink.
builder.Logging.ClearProviders();

// IncludeScopes puts the TraceId/SpanId scope into the JSON, which joins a log entry to its trace.
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

// Before the modules, so that any HttpClient they register is built on the configured defaults.
builder.Services.AddOutboundHttp();

builder.Services.AddTodoLists();
builder.Services.AddReminders();
builder.Services.AddFiles(builder.Configuration);
builder.Services.AddNotes();
// Who a due reminder rings, read from the local accounts. Neither feature names the other, so the
// host is where they meet.
builder.Services.AddScoped<IReminderRecipientDirectory, AccountReminderRecipientDirectory>();

// Not covered by the calls above; the maintenance endpoint and the maintenance job both resolve it.
builder.Services.AddPurgeExpiredIdempotencyKeys();

// In process. Registering an IDistributedCache beside this one adds a shared second level.
builder.Services.AddCacheStore();
builder.Services.AddPersistenceModule(builder.Configuration);
builder.Services.AddEmailModule(builder.Configuration);
builder.Services.AddStorageModule(builder.Configuration);

builder.Services.AddControllers();

// AddHttpPipeline leaves out telemetry and the authorisation policies: the first needs this assembly's
// name and this host's database instrumentation, the second belongs to the features that define them.
builder.Services.AddHttpPipeline(builder.Configuration);
builder.Services.AddApiObservability(builder.Configuration);

// The periodic jobs, started by this process unless BackgroundJobs:Enabled is false. One call, so a
// separate runner can make the same one. The caller and audit identity that tells a job's scope from
// a request's is registered with the HTTP pipeline above.
builder.Services.AddBackgroundJobs(builder.Configuration);

// Local accounts, whole: use cases, identity module, refresh-token purge, readiness check, the
// Administrator policy and the identity schema's development bootstrap. After the jobs, so the purge
// runs after the idempotency purge. A host without local accounts deletes this line and Features/Auth.
builder.Services.AddAuthFeature(builder.Configuration);

// Versioning first: it is what partitions actions into the version groups the loop below reads.
builder.Services.AddUrlSegmentApiVersioning();

// One document per version group. The generator that turns an XML comment into a schema's
// description hooks this call site, so the comments it collects are the ones in the assembly making
// the call: moving this loop into a referenced project drops every description and compiles.
foreach (string versionGroup in builder.Services.ApiVersionGroups())
{
    builder.Services.AddOpenApi(versionGroup, options => options.UseApiDocumentConventions(versionGroup));
}

// Authenticated by default: an endpoint reachable anonymously has to opt out explicitly.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

// AddApiHealthChecks brings the shutdown check; the business database check is this host's own, and
// each module with a store of its own adds its check with its composition.
builder.Services.AddApiHealthChecks()
    .AddDbContextCheck<AppDbContext>(name: "database", tags: [HealthEndpoints.ReadyTag]);

var app = builder.Build();

// The whole request pipeline, in the one order that works. The argument relaxes the default-deny
// content-security policy for the API-reference page alone.
app.UseHttpPipeline(
    app.Environment.IsDevelopment() ? ApiReferenceContentSecurityPolicy.For : null);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapApiHealthEndpoints();

if (app.Environment.IsDevelopment())
{
    // Without AllowAnonymous the fallback policy answers 401 to whoever opened the page.
    app.MapOpenApi().AllowAnonymous();

    // The page carries one inline module script. The nonce is what lets it run under a policy that
    // refuses every other inline script; ApiReferenceContentSecurityPolicy is where it is set.
    app.MapScalarApiReference(options => options.WithNonce()).AllowAnonymous();

    // Development only: migrating from the serving process needs DDL rights at runtime and races
    // replicas on __EFMigrationsHistory — see docs/ARCHITECTURE.md. Each module's schema and seed is
    // a step its own composition registered.
    await app.MigrateAndSeedForDevelopmentAsync();
}

await app.RunAsync();

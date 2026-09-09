using AppTemplate.Api.Common.Hosting;
using AppTemplate.Api.Common.Observability;
using AppTemplate.Api.Common.Security;
using AppTemplate.Api.Core;
using AppTemplate.Api.Core.Common.Hosting;
using AppTemplate.Application;
using AppTemplate.Application.Auth;
using AppTemplate.Application.Core;
using AppTemplate.Infrastructure.Auth;
using AppTemplate.Infrastructure.Auth.Common.Contexts;
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
builder.Services.AddFiles();
builder.Services.AddAuthApplication();

// Not covered by the calls above; the maintenance endpoint and the worker's maintenance loop both
// resolve it.
builder.Services.AddPurgeExpiredIdempotencyKeys();

// In process. Registering an IDistributedCache beside this one adds a shared second level.
builder.Services.AddCacheStore();
builder.Services.AddPersistenceModule(builder.Configuration);
builder.Services.AddAuthModule(builder.Configuration);
builder.Services.AddEmailModule(builder.Configuration);
builder.Services.AddStorageModule(builder.Configuration);

// Mandatory: no module supplies a default, so omitting it fails at composition rather than
// stamping null over every audited row.
builder.Services.AddScoped<AppTemplate.Infrastructure.Core.Common.Saving.Auditing.IAuditActor, CurrentUserAuditActor>();

builder.Services.AddControllers();

// AddApiCore leaves out telemetry and the authorisation policies: the first needs this assembly's
// name and this host's database instrumentation, the second a role name from the auth module.
builder.Services.AddApiCore(builder.Configuration);
builder.Services.AddApiObservability(builder.Configuration);

// Versioning first: it is what partitions actions into the version groups the loop below reads.
builder.Services.AddCoreApiVersioning();

// One document per version group. The generator that turns an XML comment into a schema's
// description hooks this call site, so the comments it collects are the ones in the assembly making
// the call: moving this loop into a referenced project drops every description and compiles.
foreach (string versionGroup in builder.Services.CoreApiVersionGroups())
{
    builder.Services.AddOpenApi(versionGroup, options => options.UseCoreDocumentConventions(versionGroup));
}

// Authenticated by default: an endpoint reachable anonymously has to opt out explicitly.
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

// Every AddAuthorization delegate runs against the same options, so this second call adds its
// policy without touching the fallback above.
builder.Services.AddApiAuthorizationPolicies();

// AddCoreHealthChecks brings the shutdown check; the two database checks are this host's own.
builder.Services.AddCoreHealthChecks()
    .AddDbContextCheck<AppDbContext>(name: "database", tags: [HealthEndpoints.ReadyTag])
    .AddDbContextCheck<AuthDbContext>(name: "auth-database", tags: [HealthEndpoints.ReadyTag]);

var app = builder.Build();

// The whole request pipeline, in the one order that works. The argument relaxes the default-deny
// content-security policy for the API-reference page alone.
app.UseCorePipeline(
    app.Environment.IsDevelopment() ? ApiReferenceContentSecurityPolicy.For : null);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapCoreHealthEndpoints();

if (app.Environment.IsDevelopment())
{
    // Without AllowAnonymous the fallback policy answers 401 to whoever opened the page.
    app.MapOpenApi().AllowAnonymous();

    // The page carries one inline module script. The nonce is what lets it run under a policy that
    // refuses every other inline script; ApiReferenceContentSecurityPolicy is where it is set.
    app.MapScalarApiReference(options => options.WithNonce()).AllowAnonymous();

    // Development only: migrating from the serving process needs DDL rights at runtime and races
    // replicas on __EFMigrationsHistory — see docs/ARCHITECTURE.md. Seeding is additionally gated
    // on IdentitySeed:Enabled and throws outside Development.
    await app.MigrateAndSeedForDevelopmentAsync();
}

await app.RunAsync();

/// <summary>
/// The host's entry point, named so that a test host can locate this assembly through
/// <c>WebApplicationFactory&lt;Program&gt;</c>. Top-level statements compile into a class the
/// compiler declares <c>internal</c>; this declaration makes the name public without adding a member.
/// </summary>
public partial class Program;

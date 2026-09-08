using AppTemplate.Api.Common.Hosting;
using AppTemplate.Api.Common.Observability;
using AppTemplate.Api.Common.Security;
using AppTemplate.Api.Core;
using AppTemplate.Api.Core.Common.Hosting;
using AppTemplate.Application;
using AppTemplate.Application.Auth;
using AppTemplate.Application.Core;
using AppTemplate.Infrastructure.Core;
using AppTemplate.Infrastructure.Email;
using AppTemplate.Infrastructure.Identity;
using AppTemplate.Infrastructure.Persistence;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Storage;
using AppTemplate.Presentation.Core.Common.Outbound;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Structured JSON logs. The default console formatter is unstructured, so nothing was queryable in
// a log aggregator; this is the minimum that makes production logs usable without adding a
// third-party logging dependency the template would then have to maintain.
builder.Logging.ClearProviders();

// IncludeScopes is what puts the host's TraceId/SpanId scope into the JSON, which is how a log entry
// joins the trace that produced it.
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);

// 'Server: Kestrel' names the server and its family to anyone who asks, and buys nothing.
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

// One call per module, in dependency order, and that is the whole composition. Persistence comes
// first so the ordering is visible here; the identity module also asks for it itself, idempotently,
// so it cannot be broken by being composed at the wrong moment.
//
// There is no AddTodoListsModule any more: all persistence — the to-do list feature's and the
// identity store — lives in one project behind AddPersistenceModule.
//
// A test host adds AppTemplate.Infrastructure.InMemory *after* these lines to replace the clock and the
// email sender with recording doubles. That module is deliberately not referenced by the API.

// Before the modules, so that a client any of them registers already has the budget on it. The
// storage module below is the first adapter that calls outwards, and it did not have to ask for the
// policy — that is the point of installing it on the factory's defaults.
builder.Services.AddOutboundHttp();

// One call per feature, and one for authentication. There is no call that adds all of them: a host
// composes what it offers, and a feature nothing composes is registered nowhere — which is what
// makes deleting one a folder and a line rather than an edit to a scan.
builder.Services.AddTodoLists();
builder.Services.AddReminders();
builder.Services.AddFiles();
builder.Services.AddAuthApplication();

// Opt-in, and not covered by any of the calls above: AppTemplate.Application.Core registers its one
// use case a call at a time, so a host that has no maintenance endpoint and no maintenance
// loop is not made to supply the two ports this one resolves.
builder.Services.AddPurgeExpiredIdempotencyKeys();
// The cache the tag pickers read through. In process, so nothing is deployed for it; a
// deployment that wants a shared second level registers an IDistributedCache beside this.
builder.Services.AddCacheStore();
builder.Services.AddPersistenceModule(builder.Configuration);
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddEmailModule(builder.Configuration);
builder.Services.AddStorageModule(builder.Configuration);

// Two questions with the same answer in this host, and the only registration a derived host has to
// remember: the row is stamped with whoever made the request. A default supplied by the persistence
// module would stamp null in silence where this fails at composition.
builder.Services.AddScoped<AppTemplate.Infrastructure.Core.Common.Saving.Auditing.IAuditActor, CurrentUserAuditActor>();

builder.Services.AddControllers();

// Everything an HTTP host of this template needs and configures the same way, in one call. What it
// deliberately leaves out is below: telemetry, which needs this assembly's name and this host's
// database instrumentation, and the authorisation policies, which read a role name out of the
// persistence module.
builder.Services.AddApiCore(builder.Configuration);
builder.Services.AddApiObservability(builder.Configuration);

// Versioning first: it is what partitions actions into the version groups the loop below reads.
builder.Services.AddCoreApiVersioning();

// One document per version group, and the AddOpenApi call is made here rather than inside
// AddApiCore for a reason that is invisible at the call site: the source generator that turns an
// XML comment into a schema's description hooks this call through an interceptor, so the comments
// it collects are the ones in the assembly that makes it. Called from the SDK, every request and
// response contract of this host would lose its description and nothing would fail to compile.
foreach (string versionGroup in builder.Services.CoreApiVersionGroups())
{
    builder.Services.AddOpenApi(versionGroup, options => options.UseCoreDocumentConventions(versionGroup));
}

// Authorisation: authenticated by default. This single line is what closes the template's worst
// defect — fifteen endpoints were reachable anonymously because each action was individually
// responsible for remembering [Authorize]. An endpoint must now opt out explicitly.
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

// A named policy on top of the fallback above, for the one operation that needs more than "any
// authenticated user": purging expired idempotency keys. Left as a second call rather than folded
// into the one above, so the default-deny fallback stays exactly what it was.
builder.Services.AddApiAuthorizationPolicies();

// The shutdown check comes with AddCoreHealthChecks; the database is this host's own answer to
// "can it serve traffic", so it is chained here on the builder that call returns.
builder.Services.AddCoreHealthChecks()
    .AddDbContextCheck<AppDbContext>(name: "database", tags: [HealthEndpoints.ReadyTag]);

var app = builder.Build();

// The whole request pipeline, in the one order that works, and one argument: in Development this
// host also serves an API-reference page, whose own content-security policy is the one exception to
// the default-deny policy every other response carries.
app.UseCorePipeline(
    app.Environment.IsDevelopment() ? ApiReferenceContentSecurityPolicy.For : null);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapCoreHealthEndpoints();

if (app.Environment.IsDevelopment())
{
    // Anonymous, because the fallback policy otherwise applies to these two as well and the reference
    // page answers 401 to the developer who opened it. Both are inside this branch, so neither
    // endpoint exists at all outside Development.
    app.MapOpenApi().AllowAnonymous();

    // The page has one inline <script type="module">. A per-request nonce is what lets it run under a
    // policy that still refuses every other inline script, instead of opening 'unsafe-inline'. What
    // that policy is, and how the nonce reaches the header, is ApiReferenceContentSecurityPolicy.
    app.MapScalarApiReference(options => options.WithNonce()).AllowAnonymous();

    // Development convenience only. Migrating from the process that serves requests needs DDL
    // rights at runtime and races between replicas on __EFMigrationsHistory, so production applies
    // migrations as a separate step — see docs/ARCHITECTURE.md. Seeding is additionally gated on
    // IdentitySeed:Enabled and throws outside Development.
    await app.MigrateAndSeedForDevelopmentAsync();
}

await app.RunAsync();

/// <summary>
/// The host's entry point, named so that a test host can locate this assembly through
/// <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
/// <remarks>
/// Top-level statements compile into a class the compiler declares <c>internal</c>. Declaring it
/// here makes the name public without adding a member, an <c>InternalsVisibleTo</c> or a second
/// grant out of this assembly.
/// </remarks>
public partial class Program;

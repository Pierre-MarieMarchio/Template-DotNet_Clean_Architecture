using AppTemplate.Api.Common.Hosting;
using AppTemplate.Api.Common.Observability;
using AppTemplate.Api.Common.Security;
//#if (localAuth)
using AppTemplate.Api.Features.Auth.Composition;
//#endif
using AppTemplate.Api.Features.BackgroundJobs.Jobs;
/*#if (externalAuth)
using AppTemplate.Api.Features.ExternalIdentity.Composition;
#endif*/
//#if (reminders)
using AppTemplate.Api.Features.Reminders.Directories;
//#endif
//#if (anyFeature)
using AppTemplate.Application;
//#endif
using AppTemplate.Application.Core;
//#if (reminders)
using AppTemplate.Application.Features.Reminders.Ports.ReminderRecipients;
//#endif
using AppTemplate.Infrastructure.Core;
using AppTemplate.Infrastructure.Email;
using AppTemplate.Infrastructure.Persistence;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
//#if (files)
using AppTemplate.Infrastructure.Storage;
//#endif
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

//#if (todoLists)
builder.Services.AddTodoLists();
//#endif
//#if (reminders)
builder.Services.AddReminders();
//#endif
//#if (files)
builder.Services.AddFiles(builder.Configuration);
//#endif
//#if (notes)
builder.Services.AddNotes();
//#endif
//#if (reminders)
/*#if (localAuth)*/

// Who a due reminder rings, read from the local accounts. Neither feature names the other, so the
// host is where they meet.
builder.Services.AddScoped<IReminderRecipientDirectory, AccountReminderRecipientDirectory>();
/*#else

// Who a due reminder rings, read from the address the identity provider last sent, kept on the
// caller's mapping: a background pass has no token to read it from. Neither feature names the
// other, so the host is where they meet.
builder.Services.AddScoped<IReminderRecipientDirectory, ExternalIdentityReminderRecipientDirectory>();
#endif*/
//#endif

// Not covered by the calls above; the maintenance endpoint and the maintenance job both resolve it.
builder.Services.AddPurgeExpiredIdempotencyKeys();
//#if (anyFeature)

// The sweep that guarantees the data of deleted accounts goes, whether or not the fact reached the
// feature holding it. Registered beside the purge its pass joins, and composed only with features:
// with none, there is no data an account's deletion could orphan.
builder.Services.AddDeletedAccountSweep();
//#endif

// In process. Registering an IDistributedCache beside this one adds a shared second level.
builder.Services.AddCacheStore();
builder.Services.AddPersistenceModule(builder.Configuration);
builder.Services.AddEmailModule(builder.Configuration);
//#if (files)
builder.Services.AddStorageModule(builder.Configuration);
//#endif

builder.Services.AddControllers();

// AddHttpPipeline leaves out telemetry and the authorisation policies: the first needs this assembly's
// name and this host's database instrumentation, the second belongs to the features that define them.
builder.Services.AddHttpPipeline(builder.Configuration);
builder.Services.AddApiObservability(builder.Configuration);

// The periodic jobs, started by this process unless Jobs:Enabled is false. One call, so a
// separate runner can make the same one. The caller and audit identity that tells a job's scope from
// a request's is registered with the HTTP pipeline above.
builder.Services.AddBackgroundJobs(builder.Configuration);
/*#if (localAuth)*/

// Local accounts, whole: use cases, identity module, refresh-token purge, readiness check, the
// Administrator policy and the identity schema's development bootstrap. After the jobs, so the purge
// runs after the idempotency purge. A host without local accounts deletes this line and Features/Auth.
builder.Services.AddAuthFeature(builder.Configuration);
/*#elseif (externalAuth)

// Callers authenticated by an external OpenID Connect provider: bearer validation against
// Authentication:External, validated at start-up, the mapping of each subject to an internal owner
// with its stores, and the Administrator policy on the configured role. The mapping's tables are in
// the business schema, so the business database check below covers them.
builder.Services.AddExternalAuthentication(builder.Configuration);
#endif*/

// Versioning first: it is what partitions actions into the version groups the loop below reads.
builder.Services.AddUrlSegmentApiVersioning();

// One document per version group. The generator that turns an XML comment into a schema's
// description hooks this call site, so the comments it collects are the ones in the assembly making
// the call: moving this loop into a referenced project drops every description and compiles.
foreach (string versionGroup in builder.Services.ApiVersionGroups())
{
    builder.Services.AddOpenApi(versionGroup, options => options.UseApiDocumentConventions(versionGroup));
}

/*#if (!publicApi)*/
// Authenticated by default: an endpoint reachable anonymously has to opt out explicitly.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());
/*#else
// Public by choice: generated without accounts, this API authenticates nobody, and the fallback
// policy says every endpoint answers anonymous callers rather than leaving that to a missing one.
// Nothing private is opened by it: no endpoint needs a caller, an operation that does still fails
// because ICurrentUser has none to give, and an endpoint marked [Authorize] fails rather than
// opening, since there is no scheme to challenge with. Accounts come back with an authentication
// scheme and the default-deny policy of the authenticated templates.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAssertion(static _ => true)
        .Build());
#endif*/

// AddApiHealthChecks brings the shutdown check; the business database check is this host's own, and
// each module with a store of its own adds its check with its composition.
builder.Services.AddApiHealthChecks()
    .AddDbContextCheck<AppDbContext>(name: "database", tags: [HealthEndpoints.ReadyTag]);

var app = builder.Build();

// The whole request pipeline, in the one order that works. The argument relaxes the default-deny
// content-security policy for the API-reference page alone.
app.UseHttpPipeline(
    app.Environment.IsDevelopment() ? ApiReferenceContentSecurityPolicy.For : null);

//#if (!publicApi)
app.UseAuthentication();
//#endif
app.UseAuthorization();

app.MapControllers();

app.MapApiHealthEndpoints();

if (app.Environment.IsDevelopment())
{
    //#if (!publicApi)
    // Without AllowAnonymous the fallback policy answers 401 to whoever opened the page.
    //#endif
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

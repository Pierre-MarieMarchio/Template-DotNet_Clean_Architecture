using AppTemplate.Api.Common.Caching;
using AppTemplate.Api.Common.Concurrency;
using AppTemplate.Api.Common.Errors;
using AppTemplate.Api.Common.Hosting;
using AppTemplate.Api.Common.Idempotency;
using AppTemplate.Api.Common.Localization;
using AppTemplate.Api.Common.Observability;
using AppTemplate.Api.Common.OpenApi;
using AppTemplate.Api.Common.Outbound;
using AppTemplate.Api.Common.Security;
using AppTemplate.Application;
using AppTemplate.Infrastructure.Email;
using AppTemplate.Infrastructure.Identity;
using AppTemplate.Infrastructure.Persistence;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Storage;
using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
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
// policy — that is the point of installing it on the factory's defaults. See Common/Outbound/.
builder.Services.AddApiOutboundHttp();

builder.Services.AddApplicationLayer();
builder.Services.AddPersistenceModule(builder.Configuration);
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddEmailModule(builder.Configuration);
builder.Services.AddStorageModule(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AppTemplate.Application.Common.Ports.ICurrentUser, CurrentUser>();
builder.Services.AddScoped<AppTemplate.Infrastructure.Persistence.Common.Saving.Auditing.IAuditActor, CurrentUserAuditActor>();

// Global rather than per-controller: the filter is inert on any action without [Idempotent], so
// registering it once here is safe and is one fewer thing every controller has to remember.
builder.Services.AddControllers(options => options.Filters.Add<IdempotencyFilter>());

// The other half of "no exception message reaches a client". Left on, the JSON input formatter
// copies a JsonException's text into the model error, and that text names the CLR type it was
// binding and the byte offset it stopped at. Turned off, the entry carries the exception without
// a message, and ModelStateProblemExtensions answers with its own sentence.
// Qualified: Microsoft.AspNetCore.Http.Json carries a JsonOptions of its own, and the one that
// governs a controller's input formatter is MVC's.
builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(
    options => options.AllowInputFormatterExceptionMessages = false);

// Model binding must answer the same ProblemDetails shape as an application validation failure, so
// this is wired right beside AddControllers, before anything else has a chance to bind a request.
builder.Services.AddApiModelStateProblemDetails();
builder.Services.AddRequestLanguage();

builder.Services.AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1, 0);

        // Every route template here is 'api/v{version:apiVersion}', so the version arrives in the
        // path and nowhere else. Naming the reader that reads it is not a preference: the default
        // is a composite that also inspects a 'api-version' query string and an 'x-ms-version'
        // header, and it builds that candidate set on every request for two sources this API never
        // populates. AV0015 is the analyzer that says so.
        options.ApiVersionReader = new UrlSegmentApiVersionReader();

        // Not AssumeDefaultVersionWhenUnspecified: the route template below is
        // 'api/v{version:apiVersion}', which makes the segment mandatory. A request naming no
        // version never reaches routing at all, so the option had nothing to apply to — it was
        // dead configuration, not a lenient default.
        options.ReportApiVersions = true;
    })
    .AddMvc()
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    });

// RFC 7807 for every failure, including the ones MVC produces itself (model binding, 404, 405) —
// and those get a `code` too, so a client can always branch on that field.
builder.Services.AddApiProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// One OpenAPI document per API version group, not one document for all of them: the ApiExplorer
// above already partitions actions into 'v1', 'v2', ... groups, and a single AddOpenApi() call
// captures every action regardless of its group, so a v2 added later would show up inside the v1
// document too. This is registration, not request handling, and nothing here is resolved from the
// throwaway provider except the version list itself, so the "extra singleton copy" ASP0000 warns
// about does not apply — there is no other supported way to read the version list before the real
// host exists, which is the only point AddOpenApi can still be called.
#pragma warning disable ASP0000
using (var versionProvider = builder.Services.BuildServiceProvider())
#pragma warning restore ASP0000
{
    var descriptions = versionProvider.GetRequiredService<IApiVersionDescriptionProvider>().ApiVersionDescriptions;

    foreach (var description in descriptions)
    {
        string groupName = description.GroupName;

        builder.Services.AddOpenApi(groupName, options =>
        {
            options.AddDocumentTransformer<OpenApiSecurityTransformer>();
            options.ShouldInclude = apiDescription => apiDescription.GroupName == groupName;
        });
    }
}

builder.Services.AddApiForwardedHeaders(builder.Configuration);
builder.Services.AddApiSecurityHeaders(builder.Configuration);
builder.Services.AddApiCors(builder.Configuration);
builder.Services.AddApiRateLimiting();
builder.Services.AddApiObservability(builder.Configuration);
builder.Services.AddApiConcurrency(builder.Configuration);
builder.Services.AddApiIdempotency(builder.Configuration);
builder.Services.AddApiRequestLimits(builder.Configuration);
builder.Services.AddApiLifecycle(builder.Configuration);

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

// Liveness answers "is the process up" with no dependency, so an orchestrator does not restart the
// API because the database is briefly unreachable. Readiness answers "can it serve traffic", and
// two independent things can say no: the database, and a shutdown already under way — see
// ShutdownHealthCheck for why the latter must never gate liveness too.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>(name: "database", tags: ["ready"])
    .AddCheck<ShutdownHealthCheck>(name: "shutdown", tags: ["ready"]);

var app = builder.Build();

// First, before anything reads the client address or the scheme: the rate limiter partitions on the
// remote address, and CORS, authentication and the exception handler all observe the scheme.
app.UseApiForwardedHeaders();

// Next, so that a response written by the exception handler, the rate limiter or a health endpoint
// carries the same headers as one written by a controller.
app.UseApiSecurityHeaders();

// Same reasoning and the same OnStarting mechanism as the security headers above: registered before
// UseExceptionHandler can clear the response, so an error response states Cache-Control too.
app.UseApiCacheHeaders();

// Outside the exception handler, so the entry reports the status the caller received, and ahead
// of the size limit below: that middleware answers 413 without calling the next one, so anything
// registered after it never runs on the path it rejects.
app.UseApiRequestLogging();

// Before anything reads the body, so an oversized request is refused rather than buffered.
app.UseApiRequestLimits();

app.UseExceptionHandler();
app.UseStatusCodePages();

// TLS terminates upstream and the container listens on plain 8080, so HTTPS redirection is not
// installed: it would 307 the orchestrator's health probe. Enforce HTTPS at the ingress instead.
//
// HSTS is not sent either, and not because it was forgotten: max-age, includeSubDomains and preload
// are commitments over a whole domain that this application cannot know. The component terminating
// TLS is the one that knows them and must send the header.

app.UseCors(CorsExtensions.Default);
app.UseRateLimiter();

// After the rate limiter, so a rejected request does no work; before the endpoint, because what
// this sets is what an action's mail is written in. It touches CurrentUICulture only — see
// RequestLanguageExtensions for why it deliberately is not UseRequestLocalization.
app.UseRequestLanguage();

// After the rate limiter, so a rejected request never starts a clock that then has to be torn
// down; before authentication and authorization, so the deadline covers them too, not just the
// action.
app.UseApiRequestTimeouts();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Off the rate limiter, both of them: without this, an orchestrator's probe shares the same budget
// as real traffic, and behind a mesh sidecar or a hostNetwork ingress the probe and inbound traffic
// can even share one source address and partition. A traffic spike then answers the probe 429 too,
// which the orchestrator reads as "unhealthy" on /health/ready and as "kill it" on /health — right
// as the instance is already struggling, cascading the load onto whatever replicas survive.
// ObservabilityExtensions excludes these same two paths from traces and logs for an unrelated reason
// (they would dominate every signal); this is the exclusion that keeps the instance alive.
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false,
}).AllowAnonymous().DisableRateLimiting();

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
}).AllowAnonymous().DisableRateLimiting();

if (app.Environment.IsDevelopment())
{
    // Anonymous, because the fallback policy otherwise applies to these two as well and the reference
    // page answers 401 to the developer who opened it. Both are inside this branch, so neither
    // endpoint exists at all outside Development.
    app.MapOpenApi().AllowAnonymous();

    // The page has one inline <script type="module">. A per-request nonce is what lets it run under a
    // policy that still refuses every other inline script, instead of opening 'unsafe-inline'.
    app.MapScalarApiReference(options => options.WithNonce()).AllowAnonymous();

    // Development convenience only. Migrating from the process that serves requests needs DDL
    // rights at runtime and races between replicas on __EFMigrationsHistory, so production applies
    // migrations as a separate step — see docs/ARCHITECTURE.md. Seeding is additionally gated on
    // IdentitySeed:Enabled and throws outside Development.
    await app.MigrateAndSeedForDevelopmentAsync();
}

await app.RunAsync();

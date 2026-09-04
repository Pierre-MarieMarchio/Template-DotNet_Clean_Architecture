using AppTemplate.Api.Common.Concurrency;
using AppTemplate.Api.Common.Errors;
using AppTemplate.Api.Common.Observability;
using AppTemplate.Api.Common.OpenApi;
using AppTemplate.Api.Common.Security;
using AppTemplate.Api.Common.Startup;
using AppTemplate.Application;
using AppTemplate.Infrastructure.Email;
using AppTemplate.Infrastructure.Identity;
using AppTemplate.Infrastructure.Persistence;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Asp.Versioning;
using Microsoft.EntityFrameworkCore;
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
builder.Services.AddApplicationLayer();
builder.Services.AddPersistenceModule(builder.Configuration);
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddEmailModule(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AppTemplate.Application.Common.Abstractions.ICurrentUser, CurrentUser>();

builder.Services.AddControllers();

builder.Services.AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1, 0);
        options.AssumeDefaultVersionWhenUnspecified = true;
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

builder.Services.AddOpenApi(options => options.AddDocumentTransformer<OpenApiSecurityTransformer>());

builder.Services.AddApiForwardedHeaders(builder.Configuration);
builder.Services.AddApiSecurityHeaders(builder.Configuration);
builder.Services.AddApiCors(builder.Configuration);
builder.Services.AddApiRateLimiting();
builder.Services.AddApiObservability(builder.Configuration);
builder.Services.AddApiConcurrency(builder.Configuration);

// Authorisation: authenticated by default. This single line is what closes the template's worst
// defect — fifteen endpoints were reachable anonymously because each action was individually
// responsible for remembering [Authorize]. An endpoint must now opt out explicitly.
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

// Liveness answers "is the process up" with no dependency, so an orchestrator does not restart the
// API because the database is briefly unreachable. Readiness answers "can it serve traffic".
// One check, because there is one context: two checks over one connection reported the same fact
// twice and could not disagree, so the second was noise dressed up as coverage.
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>(name: "database", tags: ["ready"]);

var app = builder.Build();

// First, before anything reads the client address or the scheme: the rate limiter partitions on the
// remote address, and CORS, authentication and the exception handler all observe the scheme.
app.UseApiForwardedHeaders();

// Next, so that a response written by the exception handler, the rate limiter or a health endpoint
// carries the same headers as one written by a controller.
app.UseApiSecurityHeaders();

// Outside the exception handler, so the entry reports the status the caller received.
app.UseApiRequestLogging();

app.UseExceptionHandler();
app.UseStatusCodePages();

// TLS terminates upstream and the container listens on plain 8080, so HTTPS redirection is not
// installed: it would 307 the orchestrator's health probe. Enforce HTTPS at the ingress instead.
//
// HSTS is not sent either, and not because it was forgotten: max-age, includeSubDomains and preload
// are commitments over a whole domain that this application cannot know. The component terminating
// TLS is the one that knows them and must send the header — see docs/adr/0012.

app.UseCors(CorsPolicies.Default);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false,
}).AllowAnonymous();

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
}).AllowAnonymous();

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

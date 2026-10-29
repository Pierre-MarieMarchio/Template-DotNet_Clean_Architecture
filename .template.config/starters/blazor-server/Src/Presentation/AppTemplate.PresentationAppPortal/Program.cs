//#if (anyFeature)
using AppTemplate.Application;
//#endif
using AppTemplate.Infrastructure.Core;
using AppTemplate.Infrastructure.Email;
using AppTemplate.Infrastructure.Persistence;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
//#if (files)
using AppTemplate.Infrastructure.Storage;
//#endif
using AppTemplate.Presentation.Core.Common.Observability;
using AppTemplate.Presentation.Core.Common.Outbound;
using AppTemplate.PresentationApp.Common.Hosting;
using AppTemplate.PresentationApp.Common.Security;
using AppTemplate.PresentationApp.Components;
//#if (localAuth)
using AppTemplate.PresentationApp.Features.Auth.Composition;
//#endif
//#if (externalAuth)
using AppTemplate.PresentationApp.Features.ExternalIdentity.Composition;
//#endif
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();

builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

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

builder.Services.AddCacheStore();
builder.Services.AddPersistenceModule(builder.Configuration);
builder.Services.AddEmailModule(builder.Configuration);
//#if (files)
builder.Services.AddStorageModule(builder.Configuration);
//#endif

builder.Services.AddCallerIdentity();
/*#if (localAuth)*/
builder.Services.AddCookieSessions(builder.Configuration);
/*#else
builder.Services.AddDataProtectionKeys(builder.Configuration);
#endif*/
//#if (externalAuth)
builder.Services.AddExternalSignIn(builder.Configuration);
//#endif

builder.Services.AddObservability(
    builder.Configuration,
    typeof(Program).Assembly,
    tracer => tracer.AddAspNetCoreInstrumentation(),
    meter => meter.AddAspNetCoreInstrumentation());

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>(name: "database", tags: [HealthEndpoints.ReadyTag]);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
//#if (!publicApi)

app.UseAuthentication();
app.UseAuthorization();
//#endif

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
/*#if (localAuth)*/

app.MapCookieSessionEndpoints();
/*#elseif (externalAuth)

app.MapExternalSignInEndpoints();
#endif*/

app.MapHealthEndpoints();

await app.RunAsync();

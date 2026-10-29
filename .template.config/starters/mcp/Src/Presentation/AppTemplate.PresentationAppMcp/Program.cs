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
//#if (localAuth)
using AppTemplate.PresentationApp.Features.Auth.Composition;
//#endif
//#if (externalAuth)
using AppTemplate.PresentationApp.Features.ExternalIdentity.Composition;
//#endif
//#if (files)
using AppTemplate.PresentationApp.Features.Files.Tools;
//#endif
//#if (notes)
using AppTemplate.PresentationApp.Features.Notes.Tools;
//#endif
//#if (reminders)
using AppTemplate.PresentationApp.Features.Reminders.Tools;
//#endif
//#if (todoLists)
using AppTemplate.PresentationApp.Features.TodoLists.Tools;
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
//#if (localAuth)
builder.Services.AddAccessKeyAuthentication(builder.Configuration);
//#endif
//#if (externalAuth)
builder.Services.AddExternalTokenAuthentication(builder.Configuration);
//#endif

builder.Services.AddObservability(
    builder.Configuration,
    typeof(Program).Assembly,
    tracer => tracer.AddAspNetCoreInstrumentation(),
    meter => meter.AddAspNetCoreInstrumentation());

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>(name: "database", tags: [HealthEndpoints.ReadyTag]);

var server = builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .AddAuthorizationFilters()
    .WithToolsListedEvenWhenNone();
//#if (todoLists)
server.WithTools<TodoListTools>();
//#endif
//#if (reminders)
server.WithTools<ReminderTools>();
//#endif
//#if (files)
server.WithTools<FileTools>();
//#endif
//#if (notes)
server.WithTools<NoteTools>();
//#endif

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapMcp(McpEndpoint.Path).RequireAuthorization();

app.MapHealthEndpoints();

await app.RunAsync();

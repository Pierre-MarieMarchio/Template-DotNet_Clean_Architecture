using AppTemplate.Application;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Infrastructure.Identity;
using AppTemplate.Infrastructure.Persistence;
using AppTemplate.Worker.Common.Maintenance;
using AppTemplate.Worker.Common.Observability;
using AppTemplate.Worker.Common.Security;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

// Structured JSON logs, same as AppTemplate.Api, for the same reason: the default console
// formatter is unstructured and nothing in it would be queryable in a log aggregator.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);

// The demonstration this whole project exists for: the same application layer, composed through
// the same infrastructure modules AppTemplate.Api uses for these two use cases, with no use case
// and no domain type touched to make it work here.
//
// AppTemplate.Infrastructure.Identity is composed only because IRefreshTokenMaintenance's sole
// adapter lives there — see AppTemplate.Worker.csproj for what that costs in configuration surface.
// There is no AddEmailModule: neither maintenance task sends mail.
builder.Services.AddApplicationLayer();
builder.Services.AddPersistenceModule(builder.Configuration);
builder.Services.AddIdentityModule(builder.Configuration);

// This host has no request and no principal — see BackgroundCurrentUser for what that means for
// a use case that reads ICurrentUser.UserId. Scoped, matching AppTemplate.Api's own registration
// of CurrentUser, even though this implementation carries no per-request state.
builder.Services.AddScoped<ICurrentUser, BackgroundCurrentUser>();

builder.Services.AddOptions<MaintenanceWorkerOptions>()
    .Bind(builder.Configuration.GetSection(MaintenanceWorkerOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<MaintenanceWorkerOptions>, MaintenanceWorkerOptionsValidator>();

// The JSON log alone is not enough: the maintenance loop only logs when a purge removes something,
// so a purge broken for weeks would otherwise be invisible. See WorkerObservability and
// MaintenanceDiagnostics.
builder.Services.AddWorkerObservability(builder.Configuration);

builder.Services.AddHostedService<MaintenanceBackgroundService>();

var host = builder.Build();

await host.RunAsync();

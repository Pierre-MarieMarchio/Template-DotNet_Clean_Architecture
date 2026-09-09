using AppTemplate.Application;
using AppTemplate.Application.Auth;
using AppTemplate.Application.Core;
using AppTemplate.Application.Core.Common.Localization;
using AppTemplate.Infrastructure.Auth;
using AppTemplate.Infrastructure.Core;
using AppTemplate.Infrastructure.Core.Common.Saving.Auditing;
using AppTemplate.Infrastructure.Email;
using AppTemplate.Infrastructure.Persistence;
using AppTemplate.Infrastructure.Storage;
using AppTemplate.Presentation.Core;
using AppTemplate.Presentation.Core.Common.Localization;
using AppTemplate.Presentation.Core.Common.Outbound;
using AppTemplate.Worker.Common.Observability;
using AppTemplate.Worker.Common.Security;
using AppTemplate.Worker.Features.Files;
using AppTemplate.Worker.Features.Maintenance;
using AppTemplate.Worker.Features.Reminders;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

// Clears the console provider the host adds by default, so the JSON one below is the only sink.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);

// AppTemplate.Infrastructure.Auth is composed for two ports, not one: IRefreshTokenMaintenanceService
// for the purge, and IUserProfilesService, which EmailReminderNotifier resolves to find the address
// a due reminder is rung at. AddAuthApplication then registers every authentication use case, so
// ValidateOnBuild requires every port that project declares to resolve here.
//
// Before the modules, so that any HttpClient they register is built on the configured defaults.
// Nothing here calls outwards today, but the first adapter that does must not be the one deciding
// what a timeout is.
builder.Services.AddOutboundHttp();

builder.Services.AddTodoLists();
builder.Services.AddReminders();
builder.Services.AddFiles();
builder.Services.AddAuthApplication();

// Not covered by the calls above; the maintenance loop resolves it.
builder.Services.AddPurgeExpiredIdempotencyKeys();

// In process. Registering an IDistributedCache beside this one adds a shared second level.
builder.Services.AddCacheStore();
builder.Services.AddPersistenceModule(builder.Configuration);
builder.Services.AddAuthModule(builder.Configuration);
builder.Services.AddEmailModule(builder.Configuration);
builder.Services.AddStorageModule(builder.Configuration);

// This host has no request and no principal, so ICurrentUser.UserId answers with nothing.
builder.Services.AddNoCallerIdentity();

// Who to record is a separate question, and this host answers it: nobody. Without this every
// commit from every loop would throw.
builder.Services.AddScoped<IAuditActor, BackgroundAuditActor>();

builder.Services.AddOptions<MaintenanceWorkerOptions>()
    .Bind(builder.Configuration.GetSection(MaintenanceWorkerOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<MaintenanceWorkerOptions>, MaintenanceWorkerOptionsValidator>();

builder.Services.AddOptions<ReminderWorkerOptions>()
    .Bind(builder.Configuration.GetSection(ReminderWorkerOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<ReminderWorkerOptions>, ReminderWorkerOptionsValidator>();

builder.Services.AddOptions<FileWorkerOptions>()
    .Bind(builder.Configuration.GetSection(FileWorkerOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<FileWorkerOptions>, FileWorkerOptionsValidator>();

// The maintenance loop logs only when a purge removes something, so a broken purge is visible in
// its counters rather than in the log.
builder.Services.AddWorkerObservability(builder.Configuration);

builder.Services.AddLocalizationOptions();

builder.Services.AddHostedService<MaintenanceBackgroundService>();
builder.Services.AddHostedService<ReminderBackgroundService>();
builder.Services.AddHostedService<FileBackgroundService>();

var host = builder.Build();

// Set once, for the process: this host serves no request, so every mail its loops send is written
// in the deployment's default — see docs/CONFIGURATION.md under `Localization`.
CurrentLanguage.Default =
    host.Services.GetRequiredService<IOptions<LocalizationOptions>>().Value.DefaultCulture;

await host.RunAsync();

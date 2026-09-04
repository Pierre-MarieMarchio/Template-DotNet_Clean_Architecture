using AppTemplate.Application;
using AppTemplate.Application.Common.Localization;
using AppTemplate.Application.Common.Ports;
using AppTemplate.Infrastructure.Email;
using AppTemplate.Infrastructure.Identity;
using AppTemplate.Infrastructure.Persistence;
using AppTemplate.Infrastructure.Persistence.Common.Saving.Auditing;
using AppTemplate.Infrastructure.Storage;
using AppTemplate.Worker.Common.Localization;
using AppTemplate.Worker.Common.Observability;
using AppTemplate.Worker.Common.Outbound;
using AppTemplate.Worker.Common.Security;
using AppTemplate.Worker.Features.Files;
using AppTemplate.Worker.Features.Maintenance;
using AppTemplate.Worker.Features.Reminders;
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
// AppTemplate.Infrastructure.Identity is composed for two reasons, and the one written here alone
// for months was the smaller: IRefreshTokenMaintenanceService's sole adapter lives there. The
// larger is that EmailReminderNotifier — the adapter behind this host's own reminder loop, not a
// favour to the API — resolves IUserProfilesService to find the address a due reminder is rung at.
// Above both, AddApplicationLayer registers every use case in the assembly, so ValidateOnBuild
// requires every port the layer declares to resolve in every host, not only the ports these two
// loops reach. TheWorkerContainer_NeedsIdentityForItsReminderLoop_NotOnlyForThePurgeAdapter holds
// that, so the claim cannot rot again. See AppTemplate.Worker.csproj for what it costs in
// configuration surface.
// AddEmailModule is here for one port: a reminder that comes due is rung by mail.
// Before the modules, so that a client any of them registers already has the budget on it. Nothing
// in this host calls outwards over IHttpClientFactory today — the storage module's SDK carries its
// own pool, see its own doc — but the policy is installed anyway, because the first adapter that
// does must not be the one that decides what a timeout is. See Common/Outbound/.
builder.Services.AddWorkerOutboundHttp();

builder.Services.AddApplicationLayer();
builder.Services.AddPersistenceModule(builder.Configuration);
builder.Services.AddIdentityModule(builder.Configuration);
builder.Services.AddEmailModule(builder.Configuration);
builder.Services.AddStorageModule(builder.Configuration);

// This host has no request and no principal — see BackgroundCurrentUser for what that means for
// a use case that reads ICurrentUser.UserId. Scoped, matching AppTemplate.Api's own registration
// of CurrentUser, even though this implementation carries no per-request state.
builder.Services.AddScoped<ICurrentUser, BackgroundCurrentUser>();

// The audit stamp is a separate question, and this host can answer it: nobody. Without this the
// interceptor would ask BackgroundCurrentUser and every commit from every loop would throw.
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

// The JSON log alone is not enough: the maintenance loop only logs when a purge removes something,
// so a purge broken for weeks would otherwise be invisible. See WorkerObservabilityExtensions and
// MaintenanceInstruments. The reminder loop logs every pass unconditionally instead — see
// ReminderBackgroundService — so it needs no such note here.
builder.Services.AddWorkerObservability(builder.Configuration);

builder.Services.AddOptions<LocalizationOptions>()
    .Bind(builder.Configuration.GetSection(LocalizationOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<LocalizationOptions>, LocalizationOptionsValidator>();

builder.Services.AddHostedService<MaintenanceBackgroundService>();
builder.Services.AddHostedService<ReminderBackgroundService>();
builder.Services.AddHostedService<FileBackgroundService>();

var host = builder.Build();

// Set once, for the process. This host serves no request, so there is no per-reader language to
// resolve: every mail its loops send is written in the deployment's default. An account that
// carried a stored language preference would be read here instead — docs/CONFIGURATION.md says so
// under `Localization`, and it is the one change that would make a reminder follow its reader.
CurrentLanguage.Default =
    host.Services.GetRequiredService<IOptions<LocalizationOptions>>().Value.DefaultCulture;

await host.RunAsync();

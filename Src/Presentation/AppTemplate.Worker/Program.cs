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

// Structured JSON logs, same as AppTemplate.Api, for the same reason: the default console
// formatter is unstructured and nothing in it would be queryable in a log aggregator.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);

// The demonstration this whole project exists for: the same application layer, composed through
// the same infrastructure modules AppTemplate.Api uses for these two use cases, with no use case
// and no domain type touched to make it work here.
//
// AppTemplate.Infrastructure.Auth is composed for two reasons, and the one written here alone
// for months was the smaller: IRefreshTokenMaintenanceService's sole adapter lives there. The
// larger is that EmailReminderNotifier — the adapter behind this host's own reminder loop, not a
// favour to the API — resolves IUserProfilesService to find the address a due reminder is rung at.
// Above both, AddAuthApplication registers every authentication use case, so ValidateOnBuild
// requires every port that project declares to resolve here, not only the ports these two loops
// reach. TheWorkerContainer_NeedsIdentityForItsReminderLoop_NotOnlyForThePurgeAdapter holds that,
// so the claim cannot rot again. See AppTemplate.Worker.csproj for what it costs in configuration
// surface.
// AddEmailModule is here for one port: a reminder that comes due is rung by mail.
// Before the modules, so that a client any of them registers already has the budget on it. Nothing
// in this host calls outwards over IHttpClientFactory today — the storage module's SDK carries its
// own pool, see its own doc — but the policy is installed anyway, because the first adapter that
// does must not be the one that decides what a timeout is. See Common/Outbound/.
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
builder.Services.AddAuthModule(builder.Configuration);
builder.Services.AddEmailModule(builder.Configuration);
builder.Services.AddStorageModule(builder.Configuration);

// This host has no request and no principal — see NoCallerCurrentUser for what that means for
// a use case that reads ICurrentUser.UserId. Scoped, matching AppTemplate.Api's own registration
// of CurrentUser, even though this implementation carries no per-request state.
builder.Services.AddNoCallerIdentity();

// The audit stamp is a separate question, and this host can answer it: nobody. Without this the
// interceptor would ask NoCallerCurrentUser and every commit from every loop would throw.
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

builder.Services.AddLocalizationOptions();

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

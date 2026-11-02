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
//#if (files)
using AppTemplate.Infrastructure.Storage;
//#endif
using AppTemplate.Presentation.Core.Common.Observability;
using AppTemplate.Presentation.Core.Common.Outbound;
using AppTemplate.Worker.Common.Security;
//#if (localAuth)
using AppTemplate.Worker.Features.Auth.Composition;
//#endif
using AppTemplate.Worker.Features.BackgroundJobs.Jobs;
//#if (externalAuth)
using AppTemplate.Worker.Features.ExternalIdentity.Composition;
//#endif
//#if (reminders)
using AppTemplate.Worker.Features.Reminders.Composition;
//#endif

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();

builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);

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

builder.Services.AddScoped<IReminderRecipientDirectory, AccountReminderRecipientDirectory>();
/*#else

builder.Services.AddScoped<IReminderRecipientDirectory, ExternalIdentityReminderRecipientDirectory>();
#endif*/
//#endif

builder.Services.AddPurgeExpiredIdempotencyKeys();
//#if (anyFeature)

builder.Services.AddDeletedAccountSweep();
//#endif

builder.Services.AddCacheStore();
builder.Services.AddPersistenceModule(builder.Configuration);
builder.Services.AddEmailModule(builder.Configuration);
//#if (files)
builder.Services.AddStorageModule(builder.Configuration);
//#endif

builder.Services.AddWorkerCallerIdentity();
/*#if (localAuth)*/

builder.Services.AddWorkerAuthentication(builder.Configuration);
/*#elseif (externalAuth)

builder.Services.AddWorkerExternalIdentity(builder.Configuration);
#endif*/

builder.Services.AddObservability(builder.Configuration, typeof(Program).Assembly);

builder.Services.AddBackgroundJobs(builder.Configuration);

using var host = builder.Build();

await host.RunAsync();

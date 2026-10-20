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
using AppTemplate.Api.Features.Reminders.Composition;
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
/*#if (publicApi)
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
#endif*/
using Scalar.AspNetCore;

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

builder.Services.AddControllers();

builder.Services.AddHttpPipeline(builder.Configuration);
builder.Services.AddApiObservability(builder.Configuration);

builder.Services.AddBackgroundJobs(builder.Configuration);
/*#if (localAuth)*/

builder.Services.AddAuthFeature(builder.Configuration);
/*#elseif (externalAuth)

builder.Services.AddExternalAuthentication(builder.Configuration);
#endif*/

builder.Services.AddUrlSegmentApiVersioning();

foreach (string versionGroup in builder.Services.ApiVersionGroups())
{
    builder.Services.AddOpenApi(versionGroup, options => options.UseApiDocumentConventions(versionGroup));
}

/*#if (!publicApi)*/
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());
/*#else
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAssertion(static _ => true)
        .Build());
#endif*/

builder.Services.AddApiHealthChecks()
    .AddDbContextCheck<AppDbContext>(name: "database", tags: [HealthEndpoints.ReadyTag]);

var app = builder.Build();

app.UseHttpPipeline(
    app.Environment.IsDevelopment() ? ApiReferenceContentSecurityPolicy.For : null);

//#if (!publicApi)
app.UseAuthentication();
//#endif
app.UseAuthorization();

app.MapControllers();

/*#if (publicApi)
var schemes = await app.Services.GetRequiredService<IAuthenticationSchemeProvider>()
    .GetAllSchemesAsync();

if (schemes.Any() is false)
{
    var unauthorized = app.Services.GetRequiredService<IActionDescriptorCollectionProvider>()
        .ActionDescriptors.Items
        .OfType<ControllerActionDescriptor>()
        .Where(action => action.EndpointMetadata.OfType<IAuthorizeData>().Any()
            && action.EndpointMetadata.OfType<IAllowAnonymous>().Any() is false)
        .Select(action => action.DisplayName)
        .Order(StringComparer.Ordinal)
        .ToList();

    if (unauthorized.Count > 0)
    {
        throw new InvalidOperationException(
            "This API authenticates nobody, so [Authorize] cannot be satisfied on: "
            + string.Join(", ", unauthorized)
            + ". Remove the attribute, or bring an authentication scheme back.");
    }
}
#endif*/

app.MapApiHealthEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();

    app.MapScalarApiReference(options => options.WithNonce()).AllowAnonymous();

    await app.MigrateAndSeedForDevelopmentAsync();
}

await app.RunAsync();

using AppTemplate.Application.Auth;
using AppTemplate.Application.Auth.Features.AccessKeys.Model.Policies;
using AppTemplate.Application.Auth.Features.Sessions.Ports.CurrentSession;
//#if (anyFeature)
using AppTemplate.Application.Core.Common.Ports;
//#endif
//#if (reminders)
using AppTemplate.Application.Features.Reminders.Ports.ReminderRecipients;
//#endif
using AppTemplate.Infrastructure.Auth;
using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Adapters.Providers;
using AppTemplate.PresentationApp.Common.Hosting;
using AppTemplate.PresentationApp.Common.Security;
//#if (reminders)
using AppTemplate.PresentationApp.Features.Reminders.Composition;
//#endif
using Microsoft.AspNetCore.Authentication;

namespace AppTemplate.PresentationApp.Features.Auth.Composition;

internal static class AccessKeyComposition
{
    internal static IServiceCollection AddAccessKeyAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddAuthApplication();

        services.AddScoped<ISecurityEventOriginProvider, HttpContextSecurityEventOriginProvider>();
        services.AddScoped<ICurrentSession, CurrentSession>();

        services.AddAuthModule(configuration);
        //#if (anyFeature)

        services.AddScoped<IUserAccountDirectory, AccountUserAccountDirectory>();
        //#endif
        //#if (reminders)
        services.AddScoped<IReminderRecipientDirectory, AccountReminderRecipientDirectory>();
        //#endif

        services.AddHealthChecks()
            .AddDbContextCheck<AuthDbContext>(name: "auth-database", tags: [HealthEndpoints.ReadyTag]);

        services.AddAuthentication(options =>
            {
                options.DefaultScheme = AccessKeyAuthenticationHandler.SchemeName;
                options.DefaultAuthenticateScheme = AccessKeyAuthenticationHandler.SchemeName;
                options.DefaultChallengeScheme = AccessKeyAuthenticationHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, AccessKeyAuthenticationHandler>(
                AccessKeyAuthenticationHandler.SchemeName,
                configureOptions: null);

        services.AddAuthorizationBuilder()
            .AddPolicy(ToolPolicies.Read, policy => policy.RequireClaim(ToolPolicies.ScopeClaim, AccessKeyPolicy.ReadScope))
            .AddPolicy(ToolPolicies.Write, policy => policy.RequireClaim(ToolPolicies.ScopeClaim, AccessKeyPolicy.WriteScope));

        return services;
    }
}

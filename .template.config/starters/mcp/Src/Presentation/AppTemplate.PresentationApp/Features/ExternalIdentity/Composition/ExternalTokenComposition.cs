using AppTemplate.Application;
//#if (anyFeature)
using AppTemplate.Application.Core.Common.Ports;
//#endif
//#if (reminders)
using AppTemplate.Application.Features.Reminders.Ports.ReminderRecipients;
//#endif
using AppTemplate.Infrastructure.Persistence;
using AppTemplate.Presentation.Core;
using AppTemplate.PresentationApp.Common.Hosting;
using AppTemplate.PresentationApp.Common.Security;
using AppTemplate.PresentationApp.Features.ExternalIdentity.Configuration.Options;
//#if (reminders)
using AppTemplate.PresentationApp.Features.Reminders.Composition;
//#endif
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore.Authentication;

namespace AppTemplate.PresentationApp.Features.ExternalIdentity.Composition;

internal static class ExternalTokenComposition
{
    internal static IServiceCollection AddExternalTokenAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddExternalIdentity();
        services.AddExternalIdentityPersistence(configuration);
        //#if (anyFeature)

        services.AddScoped<IUserAccountDirectory, ExternalIdentityUserAccountDirectory>();
        //#endif
        //#if (reminders)
        services.AddScoped<IReminderRecipientDirectory, ExternalIdentityReminderRecipientDirectory>();
        //#endif

        services.AddAuthenticationExternalOptions();
        services.AddMcpOptions(configuration);

        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
            })
            .AddJwtBearer()
            .AddMcp();

        services.AddHttpClient(ConfigureMcpJwtBearerOptions.BackchannelClientName);
        services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureMcpJwtBearerOptions>();
        services.AddSingleton<IConfigureOptions<McpAuthenticationOptions>, ConfigureMcpAuthenticationOptions>();
        services.AddScoped<ExternalPrincipalMapper>();

        services.AddAuthorizationBuilder()
            .AddPolicy(ToolPolicies.Read, policy => policy.RequireAuthenticatedUser())
            .AddPolicy(ToolPolicies.Write, policy => policy.RequireAuthenticatedUser());

        return services;
    }
}

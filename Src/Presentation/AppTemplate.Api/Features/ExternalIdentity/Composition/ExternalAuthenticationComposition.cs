using AppTemplate.Api.Common.Security;
using AppTemplate.Api.Features.ExternalIdentity.Configuration.Options;
using AppTemplate.Application;
//#if (anyFeature)
using AppTemplate.Application.Core.Common.Ports;
//#endif
using AppTemplate.Infrastructure.Persistence;
using AppTemplate.Presentation.Core;
using AppTemplate.Presentation.Core.Common.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Features.ExternalIdentity.Composition;

internal static class ExternalAuthenticationComposition
{
    internal static IServiceCollection AddExternalAuthentication(
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

        services.AddAuthenticationExternalOptions();

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
        }).AddJwtBearer();

        services.AddHttpClient(ConfigureExternalJwtBearerOptions.BackchannelClientName);
        services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureExternalJwtBearerOptions>();
        services.AddScoped<ExternalPrincipalMapper>();

        services.AddOptions<AuthorizationOptions>()
            .Configure<IOptions<AuthenticationExternalOptions>>((authorization, external) =>
                authorization.AddPolicy(
                    AuthorizationPolicies.Administrator,
                    policy => policy.RequireRole(external.Value.Roles.AdministratorRole)));

        return services;
    }
}

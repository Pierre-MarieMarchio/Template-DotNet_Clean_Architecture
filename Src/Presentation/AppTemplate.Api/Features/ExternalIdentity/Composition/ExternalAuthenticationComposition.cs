using AppTemplate.Api.Common.Security;
//#if (anyFeature)
using AppTemplate.Api.Features.ExternalIdentity.Directories;
//#endif
using AppTemplate.Api.Features.ExternalIdentity.Mapping;
using AppTemplate.Api.Features.ExternalIdentity.Options;
using AppTemplate.Application;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Features.ExternalIdentity.Composition;

/// <summary>
/// Everything the API composes to authenticate callers with an external OpenID Connect provider
/// instead of local accounts, behind one call: a host chooses this or <c>AddAuthFeature</c>, never both.
/// </summary>
internal static class ExternalAuthenticationComposition
{
    /// <summary>
    /// Registers bearer validation against <c>Authentication:External</c> (validated at start-up), the
    /// mapping of each token's subject to an internal owner with its tables' stores, the roles from
    /// the configured source, and the <see cref="AuthorizationPolicies.Administrator"/> policy.
    /// </summary>
    /// <remarks>The mapping lives in the business database, so this adds no readiness check of its own: the host's business database check already covers it.</remarks>
    internal static IServiceCollection AddExternalAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddExternalIdentity();
        services.AddExternalIdentityPersistence(configuration);

        // Whether an id still names an account, read from the mapping: the contract the features
        // reacting to a deleted account re-derive through, with the local-accounts template's
        // counterpart in <c>AuthComposition</c>. Composed with the accounts, so a feature that owns
        // user data can never resolve nothing.
        //#if (anyFeature)
        services.AddScoped<IUserAccountDirectory, ExternalIdentityUserAccountDirectory>();
        //#endif

        services.AddOptions<AuthenticationExternalOptions>()
            .Bind(configuration.GetSection(AuthenticationExternalOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AuthenticationExternalOptions>, AuthenticationExternalOptionsValidator>();

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
        }).AddJwtBearer();

        services.AddHttpClient(ConfigureExternalJwtBearerOptions.BackchannelClientName);
        services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureExternalJwtBearerOptions>();
        services.AddScoped<ExternalPrincipalMapper>();

        // Read from the validated options when the policies are built, so the role a deployment names
        // is the one required.
        services.AddOptions<AuthorizationOptions>()
            .Configure<IOptions<AuthenticationExternalOptions>>((authorization, external) =>
                authorization.AddPolicy(
                    AuthorizationPolicies.Administrator,
                    policy => policy.RequireRole(external.Value.Roles.AdministratorRole)));

        return services;
    }
}

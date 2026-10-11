using AppTemplate.Api.Common.Hosting;
using AppTemplate.Api.Common.Security;
//#if (anyFeature)
using AppTemplate.Api.Features.Auth.Directories;
//#endif
using AppTemplate.Api.Features.Auth.Jobs;
using AppTemplate.Api.Features.Auth.Providers;
using AppTemplate.Api.Features.Auth.Security;
using AppTemplate.Application.Auth;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Infrastructure.Auth;
using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.Auth.Providers;
using AppTemplate.Infrastructure.Auth.Features.Auth.Seeding;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Api.Features.Auth.Composition;

/// <summary>
/// Everything the API composes for local accounts, behind one call, so a host without them leaves
/// out this call and the <c>Features/Auth/</c> folder and nothing else.
/// </summary>
internal static class AuthComposition
{
    /// <summary>
    /// Registers the authentication use cases and module, the refresh-token purge, the readiness
    /// check on the identity database, the <see cref="AuthorizationPolicies.Administrator"/> policy,
    /// and the development step that migrates the identity schema and seeds it.
    /// </summary>
    /// <remarks>
    /// Called after <c>AddBackgroundJobs</c>, so the purge joins the maintenance timer after the
    /// idempotency purge. Inert on an instance that runs no jobs: the purge registers a task nothing
    /// reads until the timer exists.
    /// </remarks>
    internal static IServiceCollection AddAuthFeature(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddAuthApplication();

        // Before AddAuthModule, whose own origin provider is a TryAdd: this host can read the
        // current request, so the security log records where an event came from, and the module's
        // null-answer default stays for the hosts that cannot.
        services.AddScoped<ISecurityEventOriginProvider, HttpContextSecurityEventOriginProvider>();

        services.AddAuthModule(configuration);
        services.AddApiCurrentSession();
        services.AddAuthMaintenanceJobs(configuration);

        // The one half of the deleted-account reaction this host owns: whether an id still names
        // an account, for the features that re-derive their precondition through it. Composed with
        // the accounts, so a feature that owns user data can never resolve nothing.
        //#if (anyFeature)
        services.AddScoped<IUserAccountDirectory, AccountUserAccountDirectory>();
        //#endif

        services.AddHealthChecks()
            .AddDbContextCheck<AuthDbContext>(name: "auth-database", tags: [HealthEndpoints.ReadyTag]);

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.Administrator, policy => policy.RequireRole(IdentityRoles.Administrator));

        // Seeding writes accounts, so it follows the migration that creates their tables. Seeding is
        // additionally gated on IdentitySeed:Enabled and refuses to run outside Development.
        services.AddDevelopmentDatabaseStep("identity", async (provider, cancellationToken) =>
        {
            await provider.GetRequiredService<AuthDbContext>().Database.MigrateAsync(cancellationToken);
            await provider.GetRequiredService<IIdentitySeeder>().SeedAsync(cancellationToken);
        });

        return services;
    }
}

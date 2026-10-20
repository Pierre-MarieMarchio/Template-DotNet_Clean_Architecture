using AppTemplate.Api.Common.Hosting;
using AppTemplate.Api.Common.Security;
using AppTemplate.Api.Features.Auth.Jobs;
using AppTemplate.Application.Auth;
//#if (anyFeature)
using AppTemplate.Application.Core.Common.Ports;
//#endif
using AppTemplate.Infrastructure.Auth;
using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Configuration.Seeding;
using AppTemplate.Infrastructure.Auth.Features.SecurityEvents.Adapters.Providers;
using Microsoft.EntityFrameworkCore;

namespace AppTemplate.Api.Features.Auth.Composition;

internal static class AuthComposition
{
    internal static IServiceCollection AddAuthFeature(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddAuthApplication();

        services.AddScoped<ISecurityEventOriginProvider, HttpContextSecurityEventOriginProvider>();

        services.AddAuthModule(configuration);
        services.AddApiCurrentSession();
        services.AddAuthMaintenanceJobs(configuration);

        //#if (anyFeature)
        services.AddScoped<IUserAccountDirectory, AccountUserAccountDirectory>();
        //#endif

        services.AddHealthChecks()
            .AddDbContextCheck<AuthDbContext>(name: "auth-database", tags: [HealthEndpoints.ReadyTag]);

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthorizationPolicies.Administrator, policy => policy.RequireRole(IdentityRoles.Administrator));

        services.AddDevelopmentDatabaseStep("identity", async (provider, cancellationToken) =>
        {
            await provider.GetRequiredService<AuthDbContext>().Database.MigrateAsync(cancellationToken);
            await provider.GetRequiredService<IIdentitySeeder>().SeedAsync(cancellationToken);
        });

        return services;
    }
}

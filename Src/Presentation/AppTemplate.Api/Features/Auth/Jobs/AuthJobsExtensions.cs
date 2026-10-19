using AppTemplate.Api.Features.Maintenance.Jobs;
using AppTemplate.Application.Auth.Features.SecurityEvents.UseCases.Commands.PurgeExpiredSecurityEvents;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.PurgeExpiredRefreshTokens;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Features.Auth.Jobs;

internal static class AuthJobsExtensions
{
    /// <summary>
    /// Contributes authentication's two purges — the expired refresh-token grants and the
    /// aged-out security events — to the maintenance timer. This is the whole of authentication's
    /// part in background work: call it beside the rest of the authentication composition, and a
    /// project that drops authentication drops this call with it, leaving the background
    /// composition untouched.
    /// </summary>
    /// <remarks>Inert on an instance that runs no jobs: it registers a task and an option, and nothing reads either until the maintenance timer exists.</remarks>
    internal static IServiceCollection AddAuthMaintenanceJobs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<AuthPurgeOptions>()
            .Bind(configuration.GetSection(JobsMaintenanceOptions.SectionName));

        services.AddMaintenanceTask<IPurgeExpiredRefreshTokensUseCase>(
            "expired refresh-token grants",
            "Rotated and expired grants will accumulate: nothing else removes them.",
            provider => provider.GetRequiredService<IOptions<AuthPurgeOptions>>()
                .Value.PurgeExpiredRefreshTokensEnabled);

        // No leader lease, for the reason the refresh-token purge above has none: the delete is
        // idempotent, so two hosts running it at once remove each row once between them.
        services.AddMaintenanceTask<IPurgeExpiredSecurityEventsUseCase>(
            "expired security events",
            "Aged-out events will accumulate: nothing else removes them, a deleted account's included.",
            provider => provider.GetRequiredService<IOptions<AuthPurgeOptions>>()
                .Value.PurgeExpiredSecurityEventsEnabled);

        return services;
    }
}

using AppTemplate.Api.Features.Maintenance.Jobs;
using AppTemplate.Application.Auth.Features.SecurityEvents.UseCases.Commands.PurgeExpiredSecurityEvents;
using AppTemplate.Application.Auth.Features.Sessions.UseCases.Commands.PurgeExpiredRefreshTokens;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Features.Auth.Jobs;

internal static class AuthJobsExtensions
{
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

        services.AddMaintenanceTask<IPurgeExpiredSecurityEventsUseCase>(
            "expired security events",
            "Aged-out events will accumulate: nothing else removes them, a deleted account's included.",
            provider => provider.GetRequiredService<IOptions<AuthPurgeOptions>>()
                .Value.PurgeExpiredSecurityEventsEnabled);

        return services;
    }
}

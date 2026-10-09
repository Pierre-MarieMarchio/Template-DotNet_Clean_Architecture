using AppTemplate.Api.Features.Maintenance.Jobs;
using AppTemplate.Application.Auth.Features.Auth.UseCases.Commands.PurgeExpiredRefreshTokens;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Features.Auth.Jobs;

internal static class AuthJobsExtensions
{
    /// <summary>
    /// Contributes the refresh-token purge to the maintenance timer. This is the whole of
    /// authentication's part in background work: call it beside the rest of the authentication
    /// composition, and a project that drops authentication drops this call with it, leaving the
    /// background composition untouched.
    /// </summary>
    /// <remarks>
    /// Inert on an instance that runs no jobs: it registers a task and an option, and nothing reads
    /// either until the maintenance timer exists.
    /// </remarks>
    internal static IServiceCollection AddAuthMaintenanceJobs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<RefreshTokenPurgeOptions>()
            .Bind(configuration.GetSection(MaintenanceWorkerOptions.SectionName));

        services.AddMaintenanceTask<IPurgeExpiredRefreshTokensUseCase>(
            "expired refresh-token grants",
            "Rotated and expired grants will accumulate: nothing else removes them.",
            provider => provider.GetRequiredService<IOptions<RefreshTokenPurgeOptions>>()
                .Value.PurgeExpiredRefreshTokensEnabled);

        return services;
    }
}

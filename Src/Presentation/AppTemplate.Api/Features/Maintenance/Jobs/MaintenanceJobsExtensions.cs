using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;
using AppTemplate.Application.Core.Features.Maintenance.UseCases.Commands.PurgeExpiredIdempotencyKeys;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Features.Maintenance.Jobs;

internal static class MaintenanceJobsExtensions
{
    /// <summary>
    /// Binds and validates <see cref="JobsMaintenanceOptions"/>, registers the idempotency purge,
    /// and starts the timer that runs it and every other registered <see cref="IMaintenanceTask"/>.
    /// </summary>
    internal static IServiceCollection AddMaintenanceJobs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<JobsMaintenanceOptions>()
            .Bind(configuration.GetSection(JobsMaintenanceOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<JobsMaintenanceOptions>, JobsMaintenanceOptionsValidator>();

        services.AddMaintenanceTask<IPurgeExpiredIdempotencyKeysUseCase>(
            "expired idempotency keys",
            "Claimed keys will accumulate: nothing else removes an expired claim.",
            provider => provider.GetRequiredService<IOptions<JobsMaintenanceOptions>>()
                .Value.PurgeExpiredIdempotencyKeysEnabled);

        services.AddHostedService<MaintenanceBackgroundService>();

        return services;
    }

    /// <summary>
    /// Adds one purge to the maintenance timer. Tasks run in the order they were registered.
    /// </summary>
    /// <param name="services">The container being built.</param>
    /// <param name="label">What the task is called in logs, spans and counters.</param>
    /// <param name="disabledConsequence">What accumulates while the task is off.</param>
    /// <param name="isEnabled">Reads the task's own switch from the options it is bound to.</param>
    internal static IServiceCollection AddMaintenanceTask<TUseCase>(
        this IServiceCollection services,
        string label,
        string disabledConsequence,
        Func<IServiceProvider, bool> isEnabled)
        where TUseCase : IUseCase<Result<int>>
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(disabledConsequence);
        ArgumentNullException.ThrowIfNull(isEnabled);

        services.AddSingleton<IMaintenanceTask>(provider =>
            new MaintenanceTask<TUseCase>(label, disabledConsequence, () => isEnabled(provider)));

        return services;
    }
}

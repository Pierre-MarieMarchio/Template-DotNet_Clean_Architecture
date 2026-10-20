using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;
using AppTemplate.Application.Core.Features.Maintenance.UseCases.Commands.PurgeExpiredIdempotencyKeys;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Features.Maintenance.Jobs;

internal static class MaintenanceJobsExtensions
{
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

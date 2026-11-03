using AppTemplate.Api.Common.Jobs;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Features.Maintenance.UseCases.Commands.PurgeExpiredIdempotencyKeys;
using Mediator;
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

        services.AddMaintenanceTask<PurgeExpiredIdempotencyKeysCommand>(
            "expired idempotency keys",
            "Claimed keys will accumulate: nothing else removes an expired claim.",
            provider => provider.GetRequiredService<IOptions<JobsMaintenanceOptions>>()
                .Value.PurgeExpiredIdempotencyKeysEnabled);

        services.AddHostedService<MaintenanceBackgroundService>();

        return services;
    }

    internal static IServiceCollection AddMaintenanceTask<TCommand>(
        this IServiceCollection services,
        string label,
        string disabledConsequence,
        Func<IServiceProvider, bool> isEnabled)
        where TCommand : ICommand<Result<int>>, new()
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(disabledConsequence);
        ArgumentNullException.ThrowIfNull(isEnabled);

        var pass = new JobPass(
            label,
            disabledConsequence,
            MaintenanceInstruments.ActivitySource,
            "maintenance.purge",
            MaintenanceInstruments.Iterations,
            MaintenanceInstruments.Purged);

        services.AddSingleton<IMaintenanceTask>(provider =>
            new MaintenanceTask<TCommand>(pass, () => isEnabled(provider)));

        return services;
    }
}

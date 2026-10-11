using AppTemplate.Api.Features.Maintenance.Jobs;
using AppTemplate.Application.Core.Features.Accounts.UseCases.Commands.SweepDeletedAccountData;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Features.BackgroundJobs.Jobs;

internal static class DeletedAccountSweepJobsExtensions
{
    /// <summary>
    /// Contributes the deleted-account data sweep to the maintenance timer: the pass that
    /// guarantees what the <c>UserAccountDeleted</c> event only makes fast.
    /// </summary>
    /// <remarks>
    /// Not any one feature's purge, which is why it is not registered from a feature's own
    /// composition: every feature that owns user data declares its policy, and this one task
    /// sweeps them all. Registered from the background composition, gated on accounts — a host
    /// without accounts holds no data an account's deletion could orphan. Inert on an instance
    /// that runs no jobs, like every other contribution to the timer.
    /// </remarks>
    internal static IServiceCollection AddDeletedAccountSweepJobs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<DeletedAccountSweepOptions>()
            .Bind(configuration.GetSection(JobsMaintenanceOptions.SectionName));

        services.AddMaintenanceTask<ISweepDeletedAccountDataUseCase>(
            "data of deleted accounts",
            "Rows owned by an id that names no account stay where they are: the deletion event is "
            + "the fast path, and this sweep is what makes it certain.",
            provider => provider.GetRequiredService<IOptions<DeletedAccountSweepOptions>>()
                .Value.SweepDeletedAccountsDataEnabled);

        return services;
    }
}

using AppTemplate.Api.Features.Maintenance.Jobs;
using AppTemplate.Application.Core.Features.DeletedAccounts.UseCases.Commands.SweepDeletedAccountData;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Features.BackgroundJobs.Jobs;

internal static class DeletedAccountSweepJobsExtensions
{
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

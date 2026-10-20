using AppTemplate.Api.Common.Jobs;
//#if (files)
using AppTemplate.Api.Features.Files.Jobs;
//#endif
using AppTemplate.Api.Features.Maintenance.Jobs;
//#if (reminders)
using AppTemplate.Api.Features.Reminders.Jobs;
//#endif

namespace AppTemplate.Api.Features.BackgroundJobs.Jobs;

public static class BackgroundJobsModule
{
    public static IServiceCollection AddBackgroundJobs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var role = configuration.GetSection(JobsOptions.SectionName).Get<JobsOptions>()
            ?? new JobsOptions();

        services.AddOptions<JobsOptions>()
            .Bind(configuration.GetSection(JobsOptions.SectionName));

        if (!role.Enabled)
        {
            return services;
        }

        services.AddBackgroundScope();

        services.AddMaintenanceJobs(configuration);
        //#if (anyFeature)
        services.AddDeletedAccountSweepJobs(configuration);
        //#endif
        //#if (reminders)
        services.AddReminderJobs(configuration);
        //#endif
        //#if (files)
        services.AddFileJobs(configuration);
        //#endif

        services.AddBackgroundJobsTelemetry();

        return services;
    }
}

using AppTemplate.Api.Common.Jobs;
using AppTemplate.Api.Features.Files.Jobs;
using AppTemplate.Api.Features.Maintenance.Jobs;
using AppTemplate.Api.Features.Reminders.Jobs;

namespace AppTemplate.Api.Features.BackgroundJobs.Jobs;

/// <summary>
/// The one call that composes the periodic jobs: maintenance, reminders and the three file passes.
/// </summary>
/// <remarks>
/// <para>
/// Everything the jobs need is behind this call — options, validation, hosted services and
/// telemetry — so a separate runner process can make the same call over the same modules and run
/// the same jobs with no list of its own. That runner supplies its own caller and audit identity;
/// the application's use cases and storage guarantees do not change with the host.
/// </para>
/// <para>
/// A purge that belongs to another feature is not listed here. It registers itself with the
/// maintenance timer from that feature's own composition, which is what lets a project drop that
/// feature without editing this file.
/// </para>
/// </remarks>
public static class BackgroundJobsModule
{
    /// <summary>
    /// Registers the jobs when <c>BackgroundJobs:Enabled</c> is true, and nothing at all when it is
    /// not: an instance that only serves HTTP binds no job option and so requires none. When the jobs
    /// are enabled, an invalid job setting stops start-up rather than looking disabled.
    /// </summary>
    /// <param name="services">The container being built.</param>
    /// <param name="configuration">Where the role switch and every job's options are bound from.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddBackgroundJobs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var role = configuration.GetSection(BackgroundJobsOptions.SectionName).Get<BackgroundJobsOptions>()
            ?? new BackgroundJobsOptions();

        services.AddOptions<BackgroundJobsOptions>()
            .Bind(configuration.GetSection(BackgroundJobsOptions.SectionName));

        if (!role.Enabled)
        {
            return services;
        }

        services.AddBackgroundScope();

        services.AddMaintenanceJobs(configuration);
        services.AddReminderJobs(configuration);
        services.AddFileJobs(configuration);

        services.AddBackgroundJobsTelemetry();

        return services;
    }
}

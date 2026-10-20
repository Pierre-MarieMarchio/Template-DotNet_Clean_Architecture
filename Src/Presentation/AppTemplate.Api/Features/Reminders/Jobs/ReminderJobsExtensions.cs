using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Features.Reminders.Jobs;

internal static class ReminderJobsExtensions
{
    internal static IServiceCollection AddReminderJobs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<JobsRemindersOptions>()
            .Bind(configuration.GetSection(JobsRemindersOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<JobsRemindersOptions>, JobsRemindersOptionsValidator>();

        services.AddHostedService<ReminderBackgroundService>();

        return services;
    }
}

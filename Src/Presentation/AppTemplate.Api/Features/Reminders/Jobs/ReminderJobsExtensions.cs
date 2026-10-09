using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Features.Reminders.Jobs;

internal static class ReminderJobsExtensions
{
    /// <summary>
    /// Binds and validates <see cref="ReminderWorkerOptions"/> and starts the timer that fires due
    /// reminders.
    /// </summary>
    internal static IServiceCollection AddReminderJobs(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ReminderWorkerOptions>()
            .Bind(configuration.GetSection(ReminderWorkerOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<ReminderWorkerOptions>, ReminderWorkerOptionsValidator>();

        services.AddHostedService<ReminderBackgroundService>();

        return services;
    }
}

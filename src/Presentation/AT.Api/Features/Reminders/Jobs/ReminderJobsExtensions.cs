using AppTemplate.Api.Common.Jobs;
using AppTemplate.Application.Features.Reminders.UseCases.Commands.FireDueReminders;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Features.Reminders.Jobs;

internal static class ReminderJobsExtensions
{
    private static readonly JobPass _dueReminders = new(
        "due reminders",
        "Reminders that fall due will wait unsent: nothing else sends them.",
        ReminderInstruments.ActivitySource,
        "reminders.fire",
        ReminderInstruments.Iterations,
        ReminderInstruments.Notified);

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

        services.AddIntervalPass<FireDueRemindersCommand>(provider =>
        {
            var settings = provider.GetRequiredService<IOptions<JobsRemindersOptions>>().Value;

            return new IntervalPass(_dueReminders, settings.Interval, settings.Enabled);
        });

        return services;
    }
}

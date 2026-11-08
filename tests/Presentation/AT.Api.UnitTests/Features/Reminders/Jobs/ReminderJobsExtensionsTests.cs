using AppTemplate.Api.Common.Jobs;
using AppTemplate.Api.Features.Reminders.Jobs;
using AppTemplate.Application.Features.Reminders.UseCases.Commands.FireDueReminders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.Reminders.Jobs;

public sealed class ReminderJobsExtensionsTests
{
    [Fact]
    public void TheReminderPass_RunsOnItsOwnIntervalAndSwitch_AndCountsOnTheReminderMeter()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jobs:Reminders:Interval"] = "00:00:30",
                ["Jobs:Reminders:Enabled"] = "false",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddReminderJobs(configuration);
        using var provider = services.BuildServiceProvider();

        IntervalPass schedule = provider.GetServices<IHostedService>()
            .OfType<IntervalPassBackgroundService<FireDueRemindersCommand>>()
            .Single()
            .Schedule;

        schedule.Interval.ShouldBe(TimeSpan.FromSeconds(30));
        schedule.Enabled.ShouldBeFalse();
        schedule.Pass.Iterations.ShouldBeSameAs(ReminderInstruments.Iterations);
        schedule.Pass.Volume.ShouldBeSameAs(ReminderInstruments.Notified);
        schedule.Pass.ActivitySource.ShouldBeSameAs(ReminderInstruments.ActivitySource);
    }
}

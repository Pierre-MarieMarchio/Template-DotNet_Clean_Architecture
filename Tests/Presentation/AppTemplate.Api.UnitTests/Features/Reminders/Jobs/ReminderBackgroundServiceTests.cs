using AppTemplate.Api.Common.Jobs;
using AppTemplate.Api.Features.Reminders.Jobs;
using AppTemplate.Api.UnitTests.TestSupport;
using AppTemplate.Application.Features.Reminders.UseCases.Commands.FireDueReminders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.Reminders.Jobs;

public sealed class ReminderBackgroundServiceTests
{
    private static readonly TimeSpan _tinyInterval = TimeSpan.FromMilliseconds(20);

    [Fact]
    public async Task Loop_KeepsRunning_WhenTheUseCaseAlwaysThrows()
    {
        var useCase = new FakeFireDueRemindersUseCase(new InvalidOperationException("boom"));

        using var service = CreateService(useCase, EnabledOptions());

        await service.StartAsync(TestContext.Current.CancellationToken);
        await BackgroundServiceProbe.WaitUntilAsync(
            () => useCase.CallCount >= 3,
            "the reminder loop to have run three times");
        await service.StopAsync(TestContext.Current.CancellationToken);

        useCase.CallCount.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task Loop_NeverCallsTheUseCase_WhenFiringIsDisabled()
    {
        var useCase = new FakeFireDueRemindersUseCase();
        var options = EnabledOptions();
        options.Enabled = false;

        using var service = CreateService(useCase, options);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await Task.Delay(_tinyInterval * 5, TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        useCase.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task StopAsync_ReturnsPromptly_InsteadOfWaitingOutTheInterval()
    {
        var useCase = new HangingFireDueRemindersUseCase();
        var services = new ServiceCollection();
        services.AddBackgroundScope();
        services.AddScoped<IFireDueRemindersUseCase>(_ => useCase);
        using var provider = services.BuildServiceProvider();

        var options = EnabledOptions();
        options.Interval = TimeSpan.FromMinutes(10);

        using var service = new ReminderBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            NullLogger<ReminderBackgroundService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);

        await BackgroundServiceProbe.WaitUntilAsync(
            () => useCase.HasEntered,
            "the reminder pass to be in flight");

        var stopTask = service.StopAsync(TestContext.Current.CancellationToken);
        var completed = await Task.WhenAny(
            stopTask,
            Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        completed.ShouldBe(stopTask, "StopAsync must not wait out a 10-minute interval to return");
    }

    [Fact]
    public async Task Stopping_IsLogged_EvenWhenTheStopLandsMidIteration()
    {
        var useCase = new HangingFireDueRemindersUseCase();
        var services = new ServiceCollection();
        services.AddBackgroundScope();
        services.AddScoped<IFireDueRemindersUseCase>(_ => useCase);
        using var provider = services.BuildServiceProvider();

        var options = EnabledOptions();
        options.Interval = TimeSpan.FromMinutes(10);
        var logger = new ConcurrentRecordingLogger<ReminderBackgroundService>();

        using var service = new ReminderBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            logger);

        await service.StartAsync(CancellationToken.None);

        await BackgroundServiceProbe.WaitUntilAsync(
            () => useCase.HasEntered,
            "the reminder pass to be in flight");

        await service.StopAsync(CancellationToken.None);

        logger.Lines.ShouldContain(line => line.Message.Contains(
            "Reminder job stopping", StringComparison.Ordinal));
    }

    private static JobsRemindersOptions EnabledOptions() => new()
    {
        Interval = _tinyInterval,
        Enabled = true,
    };

    private static ReminderBackgroundService CreateService(
        FakeFireDueRemindersUseCase useCase,
        JobsRemindersOptions options)
    {
        var services = new ServiceCollection();
        services.AddBackgroundScope();
        services.AddScoped<IFireDueRemindersUseCase>(_ => useCase);
        var provider = services.BuildServiceProvider();

        return new ReminderBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            NullLogger<ReminderBackgroundService>.Instance);
    }

}

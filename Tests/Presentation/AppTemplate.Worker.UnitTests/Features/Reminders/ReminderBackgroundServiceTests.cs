using AppTemplate.Application.Features.Reminders.UseCases.Commands.FireDueReminders;
using AppTemplate.Worker.Features.Reminders;
using AppTemplate.Worker.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Worker.UnitTests.Features.Reminders;

/// <summary>
/// Exercises the loop itself — resilience across iterations, respect for the <c>Enabled</c> flag,
/// and respect for the stopping token — against a fake use case resolved from a real, minimal
/// container. No database: this is the same orchestration a scheduled trigger would give
/// <see cref="IFireDueRemindersUseCase"/>, run here on a timer instead.
/// </summary>
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

        // The point: a permanently failing pass does not stop the loop from reaching a later one.
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

    /// <summary>
    /// A long interval proves the point: if the host waited out the interval instead of honouring
    /// the token, this would time out. The hanging use case only ever returns via cancellation, so
    /// a clean stop is also the only way this test completes at all — proving the host does not
    /// abandon a mid-flight iteration in some half-finished state either.
    /// </summary>
    [Fact]
    public async Task StopAsync_ReturnsPromptly_InsteadOfWaitingOutTheInterval()
    {
        var services = new ServiceCollection();
        services.AddScoped<IFireDueRemindersUseCase>(_ => new HangingFireDueRemindersUseCase());
        using var provider = services.BuildServiceProvider();

        var options = EnabledOptions();
        options.Interval = TimeSpan.FromMinutes(10);

        using var service = new ReminderBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            NullLogger<ReminderBackgroundService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);

        // Give the hanging use case a moment to actually be mid-flight before asking it to stop.
        await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);

        var stopTask = service.StopAsync(TestContext.Current.CancellationToken);
        var completed = await Task.WhenAny(
            stopTask,
            Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        completed.ShouldBe(stopTask, "StopAsync must not wait out a 10-minute interval to return");
    }

    /// <summary>
    /// The stop landing while a pass is mid-flight is the case that matters: an operator watching
    /// for a loop that stopped needs the one stop that was asked for to look different from the ones
    /// that were not.
    /// </summary>
    [Fact]
    public async Task Stopping_IsLogged_EvenWhenTheStopLandsMidIteration()
    {
        var services = new ServiceCollection();
        services.AddScoped<IFireDueRemindersUseCase>(_ => new HangingFireDueRemindersUseCase());
        using var provider = services.BuildServiceProvider();

        var options = EnabledOptions();
        options.Interval = TimeSpan.FromMinutes(10);
        var logger = new RecordingLogger<ReminderBackgroundService>();

        using var service = new ReminderBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            logger);

        await service.StartAsync(CancellationToken.None);

        // Give the hanging use case a moment to actually be mid-flight before asking it to stop.
        await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);

        await service.StopAsync(CancellationToken.None);

        logger.Lines.ShouldContain(line => line.Message.Contains(
            "Reminder worker stopping", StringComparison.Ordinal));
    }

    private static ReminderWorkerOptions EnabledOptions() => new()
    {
        Interval = _tinyInterval,
        Enabled = true,
    };

    private static ReminderBackgroundService CreateService(
        FakeFireDueRemindersUseCase useCase,
        ReminderWorkerOptions options)
    {
        var services = new ServiceCollection();
        services.AddScoped<IFireDueRemindersUseCase>(_ => useCase);
        var provider = services.BuildServiceProvider();

        return new ReminderBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            NullLogger<ReminderBackgroundService>.Instance);
    }

}

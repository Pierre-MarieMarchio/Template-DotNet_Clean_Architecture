using AppTemplate.Api.Features.Maintenance.Jobs;
using AppTemplate.Api.UnitTests.TestSupport;
//#if (localAuth)
using Microsoft.Extensions.Logging;
//#endif
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.Maintenance.Jobs;

/// <summary>
/// Exercises the loop itself — resilience across iterations, respect for the toggles, and
/// respect for the stopping token — against fake use cases resolved from a real, minimal
/// container. No database, no HTTP: what runs here is the timer's own orchestration, with every
/// use case faked and every real dependency of the loop itself in place.
/// </summary>
public sealed class MaintenanceBackgroundServiceTests
{
    private static readonly TimeSpan _tinyInterval = TimeSpan.FromMilliseconds(20);

    //#if (localAuth)
    [Fact]
    public async Task Loop_KeepsRunning_WhenTheIdempotencyPurgeAlwaysThrows()
    {
        var idempotency = new FakeIdempotencyPurge(new InvalidOperationException("boom"));
        var refreshTokens = new FakeRefreshTokenPurge();
        var securityEvents = new FakeSecurityEventPurge();

        using var service = MaintenanceJobsHarness.Create(idempotency, refreshTokens, securityEvents, EnabledOptions());

        await service.StartAsync(CancellationToken.None);
        await BackgroundServiceProbe.WaitUntilAsync(
            () => refreshTokens.CallCount >= 3,
            "the refresh-token purge to have run three times");
        await service.StopAsync(CancellationToken.None);

        // The point: a permanently failing task in one iteration does not stop the loop from
        // reaching a later iteration, and does not stop its sibling task in the SAME iteration.
        idempotency.CallCount.ShouldBeGreaterThanOrEqualTo(3);
        refreshTokens.CallCount.ShouldBeGreaterThanOrEqualTo(3);
        securityEvents.CallCount.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task Loop_NeverCallsTheRefreshTokenPurge_WhenItIsDisabled()
    {
        var idempotency = new FakeIdempotencyPurge();
        var refreshTokens = new FakeRefreshTokenPurge();
        using var service = MaintenanceJobsHarness.Create(
            idempotency, refreshTokens, new FakeSecurityEventPurge(), EnabledOptions(), refreshTokensEnabled: false);

        await service.StartAsync(CancellationToken.None);
        await BackgroundServiceProbe.WaitUntilAsync(
            () => idempotency.CallCount >= 3,
            "the idempotency purge to have run three times");
        await service.StopAsync(CancellationToken.None);

        refreshTokens.CallCount.ShouldBe(0);
        idempotency.CallCount.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task Loop_NeverCallsTheIdempotencyPurge_WhenItIsDisabled()
    {
        var idempotency = new FakeIdempotencyPurge();
        var refreshTokens = new FakeRefreshTokenPurge();
        var securityEvents = new FakeSecurityEventPurge();
        var options = EnabledOptions();
        options.PurgeExpiredIdempotencyKeysEnabled = false;

        using var service = MaintenanceJobsHarness.Create(idempotency, refreshTokens, securityEvents, options);

        await service.StartAsync(CancellationToken.None);
        await BackgroundServiceProbe.WaitUntilAsync(
            () => securityEvents.CallCount >= 3,
            "the security-event purge to have run three times");
        await service.StopAsync(CancellationToken.None);

        idempotency.CallCount.ShouldBe(0);
    }

    //#endif
    /// <summary>
    /// A long interval proves the point: if the host waited out the interval instead of honouring
    /// the token, this would time out. The hanging use case only ever returns via cancellation, so
    /// a clean stop is also the only way this test completes at all — proving the host does not
    /// abandon a mid-flight iteration in some half-finished state either.
    /// </summary>
    [Fact]
    public async Task StopAsync_ReturnsPromptly_InsteadOfWaitingOutTheInterval()
    {
        var idempotencyPurge = new HangingUseCase();

        var options = EnabledOptions();
        options.Interval = TimeSpan.FromMinutes(10);

        using var service = MaintenanceJobsHarness.Create(
            idempotencyPurge,
            //#if (localAuth)
            new FakeRefreshTokenPurge(),
            new FakeSecurityEventPurge(),
            //#endif
            options);

        await service.StartAsync(CancellationToken.None);

        await BackgroundServiceProbe.WaitUntilAsync(
            () => idempotencyPurge.HasEntered,
            "the idempotency purge to be in flight");

        var stopTask = service.StopAsync(CancellationToken.None);
        var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        completed.ShouldBe(stopTask, "StopAsync must not wait out a 10-minute interval to return");
    }
    //#if (localAuth)

    /// <summary>
    /// A task switched off is a task that decided to do nothing, and it has to look different from
    /// a loop that died. The consequence is in the line because what accumulates while the purge is
    /// off is rows nothing else in the system removes.
    /// </summary>
    [Fact]
    public async Task DisabledPurge_IsLoggedWithWhatAccumulates_OnEverySkippedIteration()
    {
        var logger = new ConcurrentRecordingLogger<MaintenanceBackgroundService>();

        using var service = MaintenanceJobsHarness.Create(
            new FakeIdempotencyPurge(),
            new FakeRefreshTokenPurge(),
            new FakeSecurityEventPurge(),
            EnabledOptions(),
            refreshTokensEnabled: false,
            securityEventsEnabled: false,
            logger);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await BackgroundServiceProbe.WaitUntilAsync(
            () => logger.Lines.Any(IsTheDisabledRefreshTokenLine) && logger.Lines.Any(IsTheDisabledSecurityEventsLine),
            "both of authentication's disabled purges to be logged");
        await service.StopAsync(TestContext.Current.CancellationToken);

        var line = logger.Lines.First(IsTheDisabledRefreshTokenLine);
        line.Message.ShouldContain("disabled");
        line.Message.ShouldContain("nothing else removes them");

        var securityEventsLine = logger.Lines.First(IsTheDisabledSecurityEventsLine);
        securityEventsLine.Message.ShouldContain("disabled");
        securityEventsLine.Message.ShouldContain("nothing else removes them");
    }
    //#endif

    /// <summary>
    /// The stop landing while a purge is mid-flight is the case that matters: an operator watching
    /// for a loop that stopped needs the one stop that was asked for to look different from the ones
    /// that were not.
    /// </summary>
    [Fact]
    public async Task Stopping_IsLogged_EvenWhenTheStopLandsMidIteration()
    {
        var idempotencyPurge = new HangingUseCase();

        var options = EnabledOptions();
        options.Interval = TimeSpan.FromMinutes(10);
        var logger = new ConcurrentRecordingLogger<MaintenanceBackgroundService>();

        using var service = MaintenanceJobsHarness.Create(
            idempotencyPurge,
            //#if (localAuth)
            new FakeRefreshTokenPurge(),
            new FakeSecurityEventPurge(),
            //#endif
            options,
            //#if (localAuth)
            refreshTokensEnabled: true,
            securityEventsEnabled: true,
            //#endif
            logger: logger);

        await service.StartAsync(CancellationToken.None);

        await BackgroundServiceProbe.WaitUntilAsync(
            () => idempotencyPurge.HasEntered,
            "the idempotency purge to be in flight");

        await service.StopAsync(CancellationToken.None);

        logger.Lines.ShouldContain(line => line.Message.Contains(
            "Maintenance job stopping", StringComparison.Ordinal));
    }

    //#if (localAuth)
    private static bool IsTheDisabledRefreshTokenLine((LogLevel Level, string Message) line) =>
        line.Level == LogLevel.Warning
        && line.Message.Contains("expired refresh-token grants", StringComparison.Ordinal);

    private static bool IsTheDisabledSecurityEventsLine((LogLevel Level, string Message) line) =>
        line.Level == LogLevel.Warning
        && line.Message.Contains("expired security events", StringComparison.Ordinal);

    //#endif
    private static JobsMaintenanceOptions EnabledOptions() => new()
    {
        Interval = _tinyInterval,
        PurgeExpiredIdempotencyKeysEnabled = true,
    };
}

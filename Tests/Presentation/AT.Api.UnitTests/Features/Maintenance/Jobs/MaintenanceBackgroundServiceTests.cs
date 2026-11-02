using AppTemplate.Api.Features.Maintenance.Jobs;
using AppTemplate.Api.UnitTests.Features.BackgroundJobs.Jobs;
//#if (localAuth)
using Microsoft.Extensions.Logging;
//#endif
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.Maintenance.Jobs;

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

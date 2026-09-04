using AppTemplate.Application.Features.Maintenance.UseCases.Commands.PurgeExpiredIdempotencyKeys;
using AppTemplate.Application.Features.Maintenance.UseCases.Commands.PurgeExpiredRefreshTokens;
using AppTemplate.Worker.Common.Maintenance;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Worker.UnitTests.Common.Maintenance;

/// <summary>
/// Exercises the loop itself — resilience across iterations, respect for the toggles, and
/// respect for the stopping token — against fake use cases resolved from a real, minimal
/// container. No database, no HTTP: this is the same orchestration <c>MaintenanceController</c>
/// would trigger over HTTP, run here on a timer instead of a request.
/// </summary>
public sealed class MaintenanceBackgroundServiceTests
{
    private static readonly TimeSpan _tinyInterval = TimeSpan.FromMilliseconds(20);

    [Fact]
    public async Task Loop_KeepsRunning_WhenTheIdempotencyPurgeAlwaysThrows()
    {
        var idempotency = new FakeIdempotencyPurge(new InvalidOperationException("boom"));
        var refreshTokens = new FakeRefreshTokenPurge();

        using var service = CreateService(idempotency, refreshTokens, EnabledOptions());

        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => refreshTokens.CallCount >= 3);
        await service.StopAsync(CancellationToken.None);

        // The point: a permanently failing task in one iteration does not stop the loop from
        // reaching a later iteration, and does not stop its sibling task in the SAME iteration.
        idempotency.CallCount.ShouldBeGreaterThanOrEqualTo(3);
        refreshTokens.CallCount.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task Loop_NeverCallsTheRefreshTokenPurge_WhenItIsDisabled()
    {
        var idempotency = new FakeIdempotencyPurge();
        var refreshTokens = new FakeRefreshTokenPurge();
        var options = EnabledOptions();
        options.PurgeExpiredRefreshTokensEnabled = false;

        using var service = CreateService(idempotency, refreshTokens, options);

        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => idempotency.CallCount >= 3);
        await service.StopAsync(CancellationToken.None);

        refreshTokens.CallCount.ShouldBe(0);
        idempotency.CallCount.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task Loop_NeverCallsTheIdempotencyPurge_WhenItIsDisabled()
    {
        var idempotency = new FakeIdempotencyPurge();
        var refreshTokens = new FakeRefreshTokenPurge();
        var options = EnabledOptions();
        options.PurgeExpiredIdempotencyKeysEnabled = false;

        using var service = CreateService(idempotency, refreshTokens, options);

        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => refreshTokens.CallCount >= 3);
        await service.StopAsync(CancellationToken.None);

        idempotency.CallCount.ShouldBe(0);
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
        services.AddScoped<IPurgeExpiredIdempotencyKeysUseCase>(_ => new HangingUseCase());
        services.AddScoped<IPurgeExpiredRefreshTokensUseCase>(_ => new FakeRefreshTokenPurge());
        using var provider = services.BuildServiceProvider();

        var options = EnabledOptions();
        options.Interval = TimeSpan.FromMinutes(10);

        using var service = new MaintenanceBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            NullLogger<MaintenanceBackgroundService>.Instance);

        await service.StartAsync(CancellationToken.None);

        // Give the hanging use case a moment to actually be mid-flight before asking it to stop.
        await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);

        var stopTask = service.StopAsync(CancellationToken.None);
        var completed = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        completed.ShouldBe(stopTask, "StopAsync must not wait out a 10-minute interval to return");
    }

    private static MaintenanceWorkerOptions EnabledOptions() => new()
    {
        Interval = _tinyInterval,
        PurgeExpiredIdempotencyKeysEnabled = true,
        PurgeExpiredRefreshTokensEnabled = true,
    };

    private static MaintenanceBackgroundService CreateService(
        FakeIdempotencyPurge idempotency,
        FakeRefreshTokenPurge refreshTokens,
        MaintenanceWorkerOptions options)
    {
        var services = new ServiceCollection();
        services.AddScoped<IPurgeExpiredIdempotencyKeysUseCase>(_ => idempotency);
        services.AddScoped<IPurgeExpiredRefreshTokensUseCase>(_ => refreshTokens);
        var provider = services.BuildServiceProvider();

        return new MaintenanceBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            NullLogger<MaintenanceBackgroundService>.Instance);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (!condition())
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }
}

using AppTemplate.Application.Common.Ports;
using AppTemplate.Application.Features.Files.UseCases.Commands.PurgeAbandonedRegistrations;
using AppTemplate.Application.Features.Files.UseCases.Commands.ReclaimOrphanedContent;
using AppTemplate.Worker.Features.Files;
using AppTemplate.Worker.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Worker.UnitTests.Features.Files;

/// <summary>
/// Exercises the loops themselves — resilience across passes, respect for the toggles, respect for
/// the stopping token, and the independence of the two timers — against fake use cases resolved from
/// a real, minimal container. No database and no object store: this is the orchestration, and the
/// sweeping is the use cases' own business.
/// </summary>
public sealed class FileBackgroundServiceTests
{
    private static readonly TimeSpan _tinyInterval = TimeSpan.FromMilliseconds(20);

    [Fact]
    public async Task Loop_KeepsRunning_WhenTheOrphanSweepAlwaysThrows()
    {
        var purge = new FakeAbandonedRegistrationPurge();
        var sweep = new FakeOrphanedContentSweep(new InvalidOperationException("boom"));

        using var service = CreateService(purge, sweep, EnabledOptions());

        await service.StartAsync(TestContext.Current.CancellationToken);
        await BackgroundServiceProbe.WaitUntilAsync(
            () => purge.CallCount >= 3 && sweep.CallCount >= 3,
            "both sweeps to have run three times");
        await service.StopAsync(TestContext.Current.CancellationToken);

        // The point: a permanently failing sweep does not stop its own loop from reaching a later
        // pass, and does not stop the other loop either. The orphan sweep is the one that talks to a
        // remote store, so it is the one whose outage must not take the purge down with it.
        sweep.CallCount.ShouldBeGreaterThanOrEqualTo(3);
        purge.CallCount.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task Loop_KeepsRunning_WhenTheRegistrationPurgeAlwaysThrows()
    {
        var purge = new FakeAbandonedRegistrationPurge(new InvalidOperationException("boom"));
        var sweep = new FakeOrphanedContentSweep();

        using var service = CreateService(purge, sweep, EnabledOptions());

        await service.StartAsync(TestContext.Current.CancellationToken);
        await BackgroundServiceProbe.WaitUntilAsync(
            () => sweep.CallCount >= 3 && purge.CallCount >= 3,
            "both sweeps to have run three times");
        await service.StopAsync(TestContext.Current.CancellationToken);

        purge.CallCount.ShouldBeGreaterThanOrEqualTo(3);
        sweep.CallCount.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task Loop_NeverSweepsOrphanedContent_WhenItIsDisabled()
    {
        var purge = new FakeAbandonedRegistrationPurge();
        var sweep = new FakeOrphanedContentSweep();
        var options = EnabledOptions();
        options.ReclaimOrphanedContentEnabled = false;

        using var service = CreateService(purge, sweep, options);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await BackgroundServiceProbe.WaitUntilAsync(
            () => purge.CallCount >= 3,
            "the registration purge to have run three times");
        await service.StopAsync(TestContext.Current.CancellationToken);

        sweep.CallCount.ShouldBe(0);
        purge.CallCount.ShouldBeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task Loop_NeverPurgesRegistrations_WhenItIsDisabled()
    {
        var purge = new FakeAbandonedRegistrationPurge();
        var sweep = new FakeOrphanedContentSweep();
        var options = EnabledOptions();
        options.PurgeAbandonedRegistrationsEnabled = false;

        using var service = CreateService(purge, sweep, options);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await BackgroundServiceProbe.WaitUntilAsync(
            () => sweep.CallCount >= 3,
            "the orphan sweep to have run three times");
        await service.StopAsync(TestContext.Current.CancellationToken);

        purge.CallCount.ShouldBe(0);
        sweep.CallCount.ShouldBeGreaterThanOrEqualTo(3);
    }

    /// <summary>
    /// The two timers are independent, so a long interval on one of them must not hold the other
    /// back. Twelve hours is the shipped default for the orphan sweep, which makes this the shape of
    /// the real deployment rather than a contrived one: if the loops shared a timer, or ran in
    /// sequence on one, the purge would not reach a third pass inside this test.
    /// </summary>
    [Fact]
    public async Task RegistrationPurge_KeepsItsOwnCadence_WhileTheOrphanSweepWaitsOutALongInterval()
    {
        var purge = new FakeAbandonedRegistrationPurge();
        var sweep = new FakeOrphanedContentSweep();
        var options = EnabledOptions();
        options.ReclaimOrphanedContentInterval = TimeSpan.FromHours(12);

        using var service = CreateService(purge, sweep, options);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await BackgroundServiceProbe.WaitUntilAsync(
            () => purge.CallCount >= 3,
            "the registration purge to have run three times while the orphan sweep sleeps");
        await service.StopAsync(TestContext.Current.CancellationToken);

        purge.CallCount.ShouldBeGreaterThanOrEqualTo(3);
        sweep.CallCount.ShouldBe(1, "the orphan sweep runs once at start-up and then waits out its interval");
    }

    /// <summary>
    /// A long interval proves the point: if the host waited out the interval instead of honouring
    /// the token, this would time out. The hanging sweep only ever returns via cancellation, so a
    /// clean stop is also the only way this test completes at all — proving the host does not abandon
    /// a mid-flight pass in some half-finished state either.
    /// </summary>
    [Fact]
    public async Task StopAsync_ReturnsPromptly_InsteadOfWaitingOutTheInterval()
    {
        var services = new ServiceCollection();
        services.AddScoped<IPurgeAbandonedRegistrationsUseCase>(_ => new FakeAbandonedRegistrationPurge());
        services.AddScoped<IReclaimOrphanedContentUseCase>(_ => new HangingOrphanedContentSweep());
        using var provider = services.BuildServiceProvider();

        var options = EnabledOptions();
        options.PurgeAbandonedRegistrationsInterval = TimeSpan.FromMinutes(10);
        options.ReclaimOrphanedContentInterval = TimeSpan.FromMinutes(10);

        using var service = new FileBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            NullLogger<FileBackgroundService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);

        // Give the hanging sweep a moment to actually be mid-flight before asking it to stop.
        await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);

        var stopTask = service.StopAsync(TestContext.Current.CancellationToken);
        var completed = await Task.WhenAny(
            stopTask,
            Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        completed.ShouldBe(stopTask, "StopAsync must not wait out a 10-minute interval to return");
    }

    /// <summary>
    /// Holds the repository's decision that exclusivity between hosts belongs to the operation and
    /// not to the timer that starts it. Both use cases have written down why they take no lease; a
    /// guard added here instead would look like it made the sweeps exclusive while protecting only
    /// this host's own timer, leaving <c>MaintenanceController</c>-style callers and a second replica
    /// unaffected. If the duplicated listing of a large store ever justifies a lease, it goes inside
    /// <c>ReclaimOrphanedContentUseCase</c>, and this test is what makes that a deliberate move.
    /// </summary>
    [Fact]
    public void FileBackgroundService_TakesNoLeaderLease()
    {
        var parameters = typeof(FileBackgroundService)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .ToList();

        parameters.ShouldNotBeEmpty("this rule would pass vacuously against a type with no constructor parameters");
        parameters.Any(parameter => parameter.ParameterType == typeof(ILeaderLease)).ShouldBeFalse(
            "A leader lease taken here would guard the timer rather than the operation.");
    }

    /// <summary>
    /// Holds the coverage constraint that <c>ObjectKey.TimeSegmentFor</c> states: the orphan sweep
    /// may be made cheaper, never narrower. A file registered two years ago and deleted today has its
    /// bytes under a two-year-old prefix, so any option that let an operator restrict the sweep to
    /// recent keys — a lookback, a starting segment, a prefix — would turn the one guarantee that
    /// deleted bytes are reclaimed into a heuristic that leaks them silently. Cadence and on/off are
    /// the only two knobs this host is allowed to offer.
    /// </summary>
    [Fact]
    public void FileWorkerOptions_OffersNoWayToNarrowWhatTheOrphanSweepCovers()
    {
        string[] forbidden = ["Segment", "Prefix", "Lookback", "Since", "Oldest", "Newest", "Age"];

        var properties = typeof(FileWorkerOptions).GetProperties().Select(property => property.Name).ToList();

        properties.ShouldNotBeEmpty("this rule would pass vacuously against a type with no properties");

        var offenders = properties
            .Where(name => forbidden.Any(word => name.Contains(word, StringComparison.Ordinal)))
            .ToList();

        offenders.ShouldBeEmpty(
            "The orphan sweep's coverage is not configurable: every time slice has to be visited.");
    }

    private static FileWorkerOptions EnabledOptions() => new()
    {
        PurgeAbandonedRegistrationsInterval = _tinyInterval,
        PurgeAbandonedRegistrationsEnabled = true,
        ReclaimOrphanedContentInterval = _tinyInterval,
        ReclaimOrphanedContentEnabled = true,
    };

    private static FileBackgroundService CreateService(
        FakeAbandonedRegistrationPurge purge,
        FakeOrphanedContentSweep sweep,
        FileWorkerOptions options)
    {
        var services = new ServiceCollection();
        services.AddScoped<IPurgeAbandonedRegistrationsUseCase>(_ => purge);
        services.AddScoped<IReclaimOrphanedContentUseCase>(_ => sweep);
        var provider = services.BuildServiceProvider();

        return new FileBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            NullLogger<FileBackgroundService>.Instance);
    }
}

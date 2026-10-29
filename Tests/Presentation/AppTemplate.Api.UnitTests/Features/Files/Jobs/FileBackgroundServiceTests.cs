using AppTemplate.Api.Features.Files.Jobs;
using AppTemplate.Api.UnitTests.Features.BackgroundJobs.Jobs;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.Files.UseCases.Commands.PurgeAbandonedRegistrations;
using AppTemplate.Application.Features.Files.UseCases.Commands.ReclaimOrphanedContent;
using AppTemplate.Presentation.Core.Common.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.Files.Jobs;

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

    [Fact]
    public async Task StopAsync_ReturnsPromptly_InsteadOfWaitingOutTheInterval()
    {
        var sweep = new HangingOrphanedContentSweep();
        var services = new ServiceCollection();
        services.AddBackgroundScope();
        services.AddScoped<IPurgeAbandonedRegistrationsUseCase>(_ => new FakeAbandonedRegistrationPurge());
        services.AddScoped<IReclaimOrphanedContentUseCase>(_ => sweep);
        using var provider = services.BuildServiceProvider();

        var options = EnabledOptions();
        options.PurgeAbandonedRegistrationsInterval = TimeSpan.FromMinutes(10);
        options.ReclaimOrphanedContentInterval = TimeSpan.FromMinutes(10);

        using var service = new FileBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            NullLogger<FileBackgroundService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);

        await BackgroundServiceProbe.WaitUntilAsync(
            () => sweep.HasEntered,
            "the orphan sweep to be in flight");

        var stopTask = service.StopAsync(TestContext.Current.CancellationToken);
        var completed = await Task.WhenAny(
            stopTask,
            Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        completed.ShouldBe(stopTask, "StopAsync must not wait out a 10-minute interval to return");
    }

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

    [Fact]
    public void FileJobsOptions_OffersNoWayToNarrowWhatTheOrphanSweepCovers()
    {
        string[] forbidden = ["Segment", "Prefix", "Lookback", "Since", "Oldest", "Newest", "Age"];

        var properties = typeof(JobsFilesOptions).GetProperties().Select(property => property.Name).ToList();

        properties.ShouldNotBeEmpty("this rule would pass vacuously against a type with no properties");

        var offenders = properties
            .Where(name => forbidden.Any(word => name.Contains(word, StringComparison.Ordinal)))
            .ToList();

        offenders.ShouldBeEmpty(
            "The orphan sweep's coverage is not configurable: every time slice has to be visited.");
    }

    private static JobsFilesOptions EnabledOptions() => new()
    {
        PurgeAbandonedRegistrationsInterval = _tinyInterval,
        PurgeAbandonedRegistrationsEnabled = true,
        ReclaimOrphanedContentInterval = _tinyInterval,
        ReclaimOrphanedContentEnabled = true,
    };

    private static FileBackgroundService CreateService(
        FakeAbandonedRegistrationPurge purge,
        FakeOrphanedContentSweep sweep,
        JobsFilesOptions options)
    {
        var services = new ServiceCollection();
        services.AddBackgroundScope();
        services.AddScoped<IPurgeAbandonedRegistrationsUseCase>(_ => purge);
        services.AddScoped<IReclaimOrphanedContentUseCase>(_ => sweep);
        var provider = services.BuildServiceProvider();

        return new FileBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(options),
            NullLogger<FileBackgroundService>.Instance);
    }
}

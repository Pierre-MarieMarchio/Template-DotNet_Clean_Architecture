using System.Diagnostics.Metrics;
using AppTemplate.Api.Common.Jobs;
using AppTemplate.Api.Features.Files.Jobs;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Files.UseCases.Commands.InspectDepositedFiles;
using AppTemplate.Application.Features.Files.UseCases.Commands.PurgeAbandonedRegistrations;
using AppTemplate.Application.Features.Files.UseCases.Commands.ReclaimOrphanedContent;
using Mediator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.Files.Jobs;

public sealed class FileJobsExtensionsTests
{
    [Fact]
    public void EachFilePass_RunsOnItsOwnIntervalAndSwitch_AndCountsOnItsOwnCounter()
    {
        using var provider = Compose(new Dictionary<string, string?>
        {
            ["Jobs:Files:PurgeAbandonedRegistrationsInterval"] = "00:00:02",
            ["Jobs:Files:PurgeAbandonedRegistrationsEnabled"] = "false",
            ["Jobs:Files:ReclaimOrphanedContentInterval"] = "00:03:00",
            ["Jobs:Files:ReclaimOrphanedContentEnabled"] = "true",
            ["Jobs:Files:InspectDepositedFilesInterval"] = "00:00:40",
            ["Jobs:Files:InspectDepositedFilesEnabled"] = "false",
        });

        ShouldRun<PurgeAbandonedRegistrationsCommand>(
            provider, TimeSpan.FromSeconds(2), enabled: false, FileInstruments.RegistrationsPurged);
        ShouldRun<ReclaimOrphanedContentCommand>(
            provider, TimeSpan.FromMinutes(3), enabled: true, FileInstruments.ObjectsReclaimed);
        ShouldRun<InspectDepositedFilesCommand>(
            provider, TimeSpan.FromSeconds(40), enabled: false, FileInstruments.DepositsInspected);
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

    private static void ShouldRun<TCommand>(
        ServiceProvider provider,
        TimeSpan interval,
        bool enabled,
        Counter<long> volume)
        where TCommand : ICommand<Result<int>>, new()
    {
        IntervalPass schedule = provider.GetServices<IHostedService>()
            .OfType<IntervalPassBackgroundService<TCommand>>()
            .Single()
            .Schedule;

        schedule.Interval.ShouldBe(interval, typeof(TCommand).Name);
        schedule.Enabled.ShouldBe(enabled, typeof(TCommand).Name);
        schedule.Pass.Iterations.ShouldBeSameAs(FileInstruments.Iterations);
        schedule.Pass.Volume.ShouldBeSameAs(volume, typeof(TCommand).Name);
        schedule.Pass.ActivitySource.ShouldBeSameAs(FileInstruments.ActivitySource);
    }

    private static ServiceProvider Compose(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddFileJobs(configuration);

        return services.BuildServiceProvider();
    }
}

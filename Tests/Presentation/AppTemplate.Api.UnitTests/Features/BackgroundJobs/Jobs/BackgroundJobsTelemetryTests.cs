using AppTemplate.Api.Common.Jobs;
using AppTemplate.Api.Common.Observability;
using AppTemplate.Api.Features.BackgroundJobs.Jobs;
//#if (files)
using AppTemplate.Api.Features.Files.Jobs;
//#endif
using AppTemplate.Api.Features.Maintenance.Jobs;
//#if (reminders)
using AppTemplate.Api.Features.Reminders.Jobs;
//#endif
using AppTemplate.Presentation.Core.Common.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.BackgroundJobs.Jobs;

public sealed class BackgroundJobsTelemetryTests
{
    private const string _collector = "http://localhost:4317";

    [Fact]
    public void EnabledJobsAndTelemetry_BuildBothPipelines_AndListenToEveryJobSource()
    {
        using ServiceProvider provider = Compose(telemetryEnabled: true, jobsEnabled: true);

        provider.GetRequiredService<TracerProvider>().ShouldNotBeNull();
        provider.GetRequiredService<MeterProvider>().ShouldNotBeNull();

        //#if (files)
        FileInstruments.ActivitySource.HasListeners().ShouldBeTrue();
        //#endif
        MaintenanceInstruments.ActivitySource.HasListeners().ShouldBeTrue();
        //#if (reminders)
        ReminderInstruments.ActivitySource.HasListeners().ShouldBeTrue();
        //#endif
    }

    [Fact]
    public void DisabledJobs_LeaveTheirSourcesUnexported_WhileHttpTelemetryStillBuilds()
    {
        using ServiceProvider provider = Compose(telemetryEnabled: true, jobsEnabled: false);

        provider.GetRequiredService<TracerProvider>().ShouldNotBeNull();
        provider.GetRequiredService<MeterProvider>().ShouldNotBeNull();

        //#if (files)
        FileInstruments.ActivitySource.HasListeners().ShouldBeFalse();
        //#endif
        MaintenanceInstruments.ActivitySource.HasListeners().ShouldBeFalse();
        //#if (reminders)
        ReminderInstruments.ActivitySource.HasListeners().ShouldBeFalse();
        //#endif
    }

    [Fact]
    public void DisabledTelemetry_RegistersNoPipeline_EvenWhenTheJobsRun()
    {
        using ServiceProvider provider = Compose(telemetryEnabled: false, jobsEnabled: true);

        provider.GetService<TracerProvider>().ShouldBeNull(
            "no collector is configured, so nothing — not even the jobs' callbacks — may build an exporter");
        provider.GetService<MeterProvider>().ShouldBeNull();

        MaintenanceInstruments.ActivitySource.HasListeners().ShouldBeFalse();
    }

    private static ServiceProvider Compose(bool telemetryEnabled, bool jobsEnabled)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{TelemetryOptions.SectionName}:{nameof(TelemetryOptions.Enabled)}"] =
                    telemetryEnabled ? "true" : "false",
                [$"{TelemetryOptions.SectionName}:{nameof(TelemetryOptions.OtlpEndpoint)}"] =
                    telemetryEnabled ? _collector : null,
                [$"{JobsOptions.SectionName}:{nameof(JobsOptions.Enabled)}"] =
                    jobsEnabled ? "true" : "false",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApiObservability(configuration);
        services.AddBackgroundJobs(configuration);

        return services.BuildServiceProvider();
    }
}

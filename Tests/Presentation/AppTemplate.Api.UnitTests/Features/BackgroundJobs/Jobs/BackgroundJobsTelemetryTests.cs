using AppTemplate.Api.Common.Jobs;
using AppTemplate.Api.Common.Observability;
using AppTemplate.Api.Features.BackgroundJobs.Jobs;
using AppTemplate.Api.Features.Files.Jobs;
using AppTemplate.Api.Features.Maintenance.Jobs;
using AppTemplate.Api.Features.Reminders.Jobs;
using AppTemplate.Presentation.Core.Common.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Features.BackgroundJobs.Jobs;

/// <summary>
/// The sources and meters the jobs add to the API's telemetry registration, and that the pipeline
/// still builds with them in it.
/// <para>
/// <c>ObservabilityRegistrationTests</c> in the architecture project reads the registration calls
/// out of the source and holds every instrument the jobs declare against them. What it cannot do
/// is resolve the result: an <c>AddMeter</c> naming a meter that exists, in a pipeline that throws
/// on construction, satisfies the text and exports nothing. This resolves it, and asks the runtime
/// whether anything now listens to each job's <see cref="System.Diagnostics.ActivitySource"/>.
/// </para>
/// </summary>
public sealed class BackgroundJobsTelemetryTests
{
    /// <summary>The endpoint shape the options validator accepts — absolute, http or https.</summary>
    private const string _collector = "http://localhost:4317";

    [Fact]
    public void EnabledJobsAndTelemetry_BuildBothPipelines_AndListenToEveryJobSource()
    {
        using ServiceProvider provider = Compose(telemetryEnabled: true, jobsEnabled: true);

        // Resolving is what constructs them, so this is the assertion: an AddSource or AddMeter
        // naming something unresolvable, or AddNpgsql against a driver that is not there, throws
        // here rather than at the first measurement in production.
        provider.GetRequiredService<TracerProvider>().ShouldNotBeNull();
        provider.GetRequiredService<MeterProvider>().ShouldNotBeNull();

        FileInstruments.ActivitySource.HasListeners().ShouldBeTrue();
        MaintenanceInstruments.ActivitySource.HasListeners().ShouldBeTrue();
        ReminderInstruments.ActivitySource.HasListeners().ShouldBeTrue();
    }

    [Fact]
    public void DisabledJobs_LeaveTheirSourcesUnexported_WhileHttpTelemetryStillBuilds()
    {
        using ServiceProvider provider = Compose(telemetryEnabled: true, jobsEnabled: false);

        provider.GetRequiredService<TracerProvider>().ShouldNotBeNull();
        provider.GetRequiredService<MeterProvider>().ShouldNotBeNull();

        FileInstruments.ActivitySource.HasListeners().ShouldBeFalse();
        MaintenanceInstruments.ActivitySource.HasListeners().ShouldBeFalse();
        ReminderInstruments.ActivitySource.HasListeners().ShouldBeFalse();
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
                [$"{BackgroundJobsOptions.SectionName}:{nameof(BackgroundJobsOptions.Enabled)}"] =
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

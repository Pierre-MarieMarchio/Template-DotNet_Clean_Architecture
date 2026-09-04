using AppTemplate.Worker.Common.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Shouldly;
using Xunit;

namespace AppTemplate.Worker.UnitTests.Common.Observability;

/// <summary>
/// That the telemetry section decides whether this host exports anything, and that the pipeline it
/// builds when told to actually builds.
/// <para>
/// <c>ObservabilityRegistrationTests</c> in the architecture project reads the registration calls out
/// of the source and holds every instrument this host declares against them. What it cannot do is
/// resolve the result: an <c>AddMeter</c> naming a meter that exists, in a pipeline that throws on
/// construction, satisfies the text and exports nothing. This resolves it.
/// </para>
/// </summary>
public sealed class WorkerObservabilityExtensionsTests
{
    /// <summary>The endpoint shape the options validator accepts — absolute, http or https.</summary>
    private const string _collector = "http://localhost:4317";

    [Fact]
    public void Disabled_BindsTheOptions_AndRegistersNoExporter()
    {
        using ServiceProvider provider = Compose(enabled: false);

        // The options and their validator are registered either way: the section has to be readable
        // and rejectable before anything decides what to do with it.
        provider.GetRequiredService<IOptions<WorkerTelemetryOptions>>().Value.Enabled.ShouldBeFalse();
        provider.GetServices<IValidateOptions<WorkerTelemetryOptions>>().ShouldNotBeEmpty();

        // No collector, no exporter — the early return, which is what keeps a default deployment from
        // spending anything on telemetry it has nowhere to send.
        provider.GetService<MeterProvider>().ShouldBeNull();
        provider.GetService<TracerProvider>().ShouldBeNull();
    }

    [Fact]
    public void Enabled_BuildsBothPipelines()
    {
        using ServiceProvider provider = Compose(enabled: true);

        // Resolving is what constructs them, so this is the assertion: an AddMeter or AddSource
        // naming something unresolvable, or an exporter given an endpoint it cannot parse, throws
        // here rather than at the first measurement in production.
        provider.GetRequiredService<MeterProvider>().ShouldNotBeNull();
        provider.GetRequiredService<TracerProvider>().ShouldNotBeNull();
    }

    private static ServiceProvider Compose(bool enabled)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{WorkerTelemetryOptions.SectionName}:Enabled"] = enabled ? "true" : "false",
                [$"{WorkerTelemetryOptions.SectionName}:OtlpEndpoint"] = _collector,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWorkerObservability(configuration);

        return services.BuildServiceProvider();
    }
}

using AppTemplate.Presentation.Core.Common.Observability;
using AppTemplate.Worker.Common.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Shouldly;
using Xunit;

namespace AppTemplate.Worker.UnitTests.Common.Observability;

/// <summary>
/// The sources, meters and database instrumentation this host adds on top of the shared
/// registration, and that the pipeline still builds with them in it.
/// <para>
/// <c>ObservabilityRegistrationTests</c> in the architecture project reads the registration calls
/// out of the source and holds every instrument this host declares against them. What it cannot do
/// is resolve the result: an <c>AddMeter</c> naming a meter that exists, in a pipeline that throws
/// on construction, satisfies the text and exports nothing. This resolves it.
/// </para>
/// </summary>
public sealed class WorkerObservabilityExtensionsTests
{
    /// <summary>The endpoint shape the options validator accepts — absolute, http or https.</summary>
    private const string _collector = "http://localhost:4317";

    [Fact]
    public void Enabled_BuildsBothPipelines_WithThisHostsOwnInstruments()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{TelemetryOptions.SectionName}:{nameof(TelemetryOptions.Enabled)}"] = "true",
                [$"{TelemetryOptions.SectionName}:{nameof(TelemetryOptions.OtlpEndpoint)}"] = _collector,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWorkerObservability(configuration);

        using ServiceProvider provider = services.BuildServiceProvider();

        // Resolving is what constructs them, so this is the assertion: an AddSource or AddMeter
        // naming something unresolvable, or AddNpgsql against a driver that is not there, throws
        // here rather than at the first measurement in production.
        provider.GetRequiredService<TracerProvider>().ShouldNotBeNull();
        provider.GetRequiredService<MeterProvider>().ShouldNotBeNull();
    }
}

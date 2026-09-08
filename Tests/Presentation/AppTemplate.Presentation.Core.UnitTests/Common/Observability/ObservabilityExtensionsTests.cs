using System.Reflection;
using AppTemplate.Presentation.Core.Common.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Shouldly;
using Xunit;

namespace AppTemplate.Presentation.Core.UnitTests.Common.Observability;

/// <summary>
/// That the telemetry section decides whether a host exports anything, that the pipeline it builds
/// when told to actually builds, and that the process announces itself under the name of the
/// assembly the caller named.
/// </summary>
public sealed class ObservabilityExtensionsTests
{
    /// <summary>The endpoint shape the options validator accepts — absolute, http or https.</summary>
    private const string _collector = "http://localhost:4317";

    private const string _serviceNameAttribute = "service.name";

    [Fact]
    public void Disabled_BindsTheOptions_AndRegistersNoExporter()
    {
        using ServiceProvider provider = Compose(enabled: false);

        // The options and their validator are registered either way: the section has to be readable
        // and rejectable before anything decides what to do with it.
        provider.GetRequiredService<IOptions<TelemetryOptions>>().Value.Enabled.ShouldBeFalse();
        provider.GetServices<IValidateOptions<TelemetryOptions>>().ShouldNotBeEmpty();

        // No collector, no exporter — the early return, which is what keeps a default deployment from
        // spending anything on telemetry it has nowhere to send.
        provider.GetService<MeterProvider>().ShouldBeNull();
        provider.GetService<TracerProvider>().ShouldBeNull();
    }

    [Fact]
    public void Enabled_BuildsBothPipelines()
    {
        using ServiceProvider provider = Compose(enabled: true);

        // Resolving is what constructs them, so this is the assertion: an exporter given an endpoint
        // it cannot parse, or instrumentation that fails to install, throws here rather than at the
        // first measurement in production.
        provider.GetRequiredService<MeterProvider>().ShouldNotBeNull();
        provider.GetRequiredService<TracerProvider>().ShouldNotBeNull();
    }

    /// <summary>
    /// The registration takes an assembly precisely so that two hosts do not report under one name.
    /// Two different assemblies, two different <c>service.name</c> values, read off the resource the
    /// tracer was built with.
    /// </summary>
    [Fact]
    public void TheServiceName_IsTheNameOfTheAssemblyTheCallerNamed()
    {
        Assembly caller = typeof(ObservabilityExtensionsTests).Assembly;
        Assembly other = typeof(TelemetryOptions).Assembly;

        using ServiceProvider provider = Compose(enabled: true, host: caller);
        using ServiceProvider otherProvider = Compose(enabled: true, host: other);

        ServiceNameOf(provider).ShouldBe(caller.GetName().Name);
        ServiceNameOf(otherProvider).ShouldBe(other.GetName().Name);
    }

    /// <summary>
    /// The override is what several deployments sharing one collector need, so it has to win over
    /// the assembly rather than sit alongside it.
    /// </summary>
    [Fact]
    public void TheConfiguredServiceName_OverridesTheAssemblyName()
    {
        const string configured = "checkout-eu-west-1";

        using ServiceProvider provider = Compose(
            enabled: true,
            host: typeof(ObservabilityExtensionsTests).Assembly,
            serviceName: configured);

        ServiceNameOf(provider).ShouldBe(configured);
    }

    /// <summary>
    /// The resource of the tracer the container hands out — the value that reaches a collector, not
    /// a re-reading of the options the test itself supplied.
    /// </summary>
    private static object? ServiceNameOf(ServiceProvider provider)
    {
        Resource resource = provider.GetRequiredService<TracerProvider>().GetResource();

        return resource.Attributes
            .Single(attribute => attribute.Key == _serviceNameAttribute)
            .Value;
    }

    private static ServiceProvider Compose(
        bool enabled,
        Assembly? host = null,
        string? serviceName = null)
    {
        var settings = new Dictionary<string, string?>
        {
            [$"{TelemetryOptions.SectionName}:{nameof(TelemetryOptions.Enabled)}"] = enabled ? "true" : "false",
            [$"{TelemetryOptions.SectionName}:{nameof(TelemetryOptions.OtlpEndpoint)}"] = _collector,
        };

        if (serviceName is not null)
        {
            settings[$"{TelemetryOptions.SectionName}:{nameof(TelemetryOptions.ServiceName)}"] = serviceName;
        }

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddObservability(
            configuration,
            host ?? typeof(ObservabilityExtensionsTests).Assembly);

        return services.BuildServiceProvider();
    }
}

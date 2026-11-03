using System.Reflection;
using AppTemplate.Presentation.Core.Common.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Shouldly;
using Xunit;

namespace AppTemplate.Presentation.Core.UnitTests.Common.Observability;

public sealed class ObservabilityExtensionsTests
{
    private const string _collector = "http://localhost:4317";

    private const string _forgedPath = "/a\r\nFORGED";

    private const string _serviceNameAttribute = "service.name";

    [Fact]
    public void Disabled_BindsTheOptions_AndRegistersNoExporter()
    {
        using ServiceProvider provider = Compose(enabled: false);

        provider.GetRequiredService<IOptions<TelemetryOptions>>().Value.Enabled.ShouldBeFalse();
        provider.GetServices<IValidateOptions<TelemetryOptions>>().ShouldNotBeEmpty();

        provider.GetService<MeterProvider>().ShouldBeNull();
        provider.GetService<TracerProvider>().ShouldBeNull();
        provider.GetService<LoggerProvider>().ShouldBeNull();
        provider.GetServices<ILoggerProvider>().OfType<OpenTelemetryLoggerProvider>().ShouldBeEmpty();
    }

    [Fact]
    public void Enabled_BuildsBothPipelines()
    {
        using ServiceProvider provider = Compose(enabled: true);

        provider.GetRequiredService<MeterProvider>().ShouldNotBeNull();
        provider.GetRequiredService<TracerProvider>().ShouldNotBeNull();
    }

    [Fact]
    public void Enabled_BuildsTheLogPipeline_AndHooksItToTheLoggingSystem()
    {
        using ServiceProvider provider = Compose(enabled: true);

        provider.GetRequiredService<LoggerProvider>().ShouldNotBeNull();
        provider.GetServices<ILoggerProvider>().OfType<OpenTelemetryLoggerProvider>().ShouldHaveSingleItem();
    }

    [Fact]
    public void TheExportedLogs_CarryTheFormattedMessageAndTheScopes_AndTheValuesAsAttributes()
    {
        var records = new List<ExportedLog>();

        using ServiceProvider provider = Compose(enabled: true, capture: records);

        ILogger logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Probe");

        using (logger.BeginScope("Tenant {Tenant}", "north"))
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Reached {Path}", _forgedPath);
            }
        }

        ExportedLog exported = records.ShouldHaveSingleItem();

        exported.FormattedMessage.ShouldBe($"Reached {_forgedPath}");
        exported.Attributes.ShouldContain(new KeyValuePair<string, object?>("Path", _forgedPath));
        exported.Scopes.ShouldContain(new KeyValuePair<string, object?>("Tenant", "north"));
    }

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
        string? serviceName = null,
        List<ExportedLog>? capture = null)
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

        if (capture is not null)
        {
            services.ConfigureOpenTelemetryLoggerProvider(
                builder => builder.AddProcessor(new CapturingProcessor(capture)));
        }

        services.AddObservability(
            configuration,
            host ?? typeof(ObservabilityExtensionsTests).Assembly);

        return services.BuildServiceProvider();
    }

    private sealed record ExportedLog(
        string? FormattedMessage,
        IReadOnlyList<KeyValuePair<string, object?>> Attributes,
        IReadOnlyList<KeyValuePair<string, object?>> Scopes);

    private sealed class CapturingProcessor(List<ExportedLog> records) : BaseProcessor<LogRecord>
    {
        public override void OnEnd(LogRecord data)
        {
            var scopes = new List<KeyValuePair<string, object?>>();

            data.ForEachScope(
                static (scope, list) =>
                {
                    foreach (KeyValuePair<string, object?> pair in scope)
                    {
                        list.Add(pair);
                    }
                },
                scopes);

            records.Add(new ExportedLog(
                data.FormattedMessage,
                data.Attributes?.ToList() ?? [],
                scopes));
        }
    }
}

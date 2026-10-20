using AppTemplate.Api.Common.Hosting;
using AppTemplate.Presentation.Core.Common.Observability;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace AppTemplate.Api.Common.Observability;

public static class ObservabilityExtensions
{
    private const string _traceIdentifierTagName = "aspnetcore.trace_identifier";

    public static IServiceCollection AddApiObservability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return services.AddObservability(
            configuration,
            typeof(ObservabilityExtensions).Assembly,
            tracer =>
            {
                tracer.AddAspNetCoreInstrumentation(instrumentation =>
                {
                    instrumentation.Filter = static context => !IsHealthProbe(context.Request.Path);

                    instrumentation.EnrichWithHttpRequest = static (activity, request) =>
                        activity.SetTag(_traceIdentifierTagName, request.HttpContext.TraceIdentifier);
                });

                tracer.AddNpgsql();
            },
            meter =>
            {
                meter.AddAspNetCoreInstrumentation()
                    .AddMeter("Microsoft.AspNetCore.RateLimiting");

                meter.AddMeter("Npgsql");
            });
    }

    internal static WebApplication UseApiRequestLogging(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseWhen(
            static context => !IsHealthProbe(context.Request.Path),
            branch => branch.UseMiddleware<RequestLoggingMiddleware>());

        return app;
    }

    private static bool IsHealthProbe(PathString path) =>
        path.StartsWithSegments(HealthEndpoints.PathPrefix, StringComparison.OrdinalIgnoreCase);
}

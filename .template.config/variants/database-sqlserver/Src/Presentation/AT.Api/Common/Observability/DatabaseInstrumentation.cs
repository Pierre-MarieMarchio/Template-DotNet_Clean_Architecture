using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace AppTemplate.Api.Common.Observability;

internal static class DatabaseInstrumentation
{
    internal static TracerProviderBuilder AddDatabaseInstrumentation(this TracerProviderBuilder tracer) =>
        tracer.AddSqlClientInstrumentation();

    internal static MeterProviderBuilder AddDatabaseInstrumentation(this MeterProviderBuilder meter) => meter;
}

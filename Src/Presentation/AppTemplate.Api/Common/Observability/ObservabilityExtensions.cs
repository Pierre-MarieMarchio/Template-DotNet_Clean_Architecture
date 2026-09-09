using AppTemplate.Api.Core.Common.Observability;
using Npgsql;

namespace AppTemplate.Api.Common.Observability;

/// <summary>
/// What only this host can say about its own telemetry: which database it talks to, and under which
/// name it announces itself. Everything an HTTP host has in common with any other lives in
/// <see cref="AppTemplate.Api.Core.Common.Observability.ObservabilityExtensions"/>.
/// </summary>
/// <remarks>
/// <c>service.name</c> is read from the assembly passed in, so each host reports under its own name.
/// The database instrumentation is registered here rather than in the shared project because its
/// package carries the PostgreSQL driver.
/// </remarks>
public static class ObservabilityExtensions
{
    public static IServiceCollection AddApiObservability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return services.AddCoreObservability(
            configuration,
            typeof(ObservabilityExtensions).Assembly,
            // Npgsql's own ActivitySource, which spans the command at the ADO.NET level — where the
            // SQL and its duration are. It needs no EF Core instrumentation on top.
            tracing => tracing.AddNpgsql(),
            // db.client.connection.count vs .max is the pool-saturation question Database:MaxPoolSize
            // exists to answer; db.client.connection.npgsql.pending_requests is callers already queued
            // for a connection — both invisible from the span above, which only starts once a
            // connection has been handed out.
            metrics => metrics.AddMeter("Npgsql"));
    }
}

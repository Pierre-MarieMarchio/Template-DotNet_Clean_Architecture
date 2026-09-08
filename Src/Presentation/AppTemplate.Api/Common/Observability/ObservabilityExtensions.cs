using AppTemplate.Api.Core.Common.Observability;
using Npgsql;

namespace AppTemplate.Api.Common.Observability;

/// <summary>
/// What only this host can say about its own telemetry: which database it talks to, and under which
/// name it announces itself. Everything an HTTP host has in common with any other lives in
/// <see cref="AppTemplate.Api.Core.Common.Observability.ObservabilityExtensions"/>.
/// </summary>
/// <remarks>
/// The database instrumentation stays here because its package pulls the PostgreSQL driver, and a
/// shared presentation project would then hand a database driver to any host that referenced it.
/// The assembly is passed for a second reason: <c>service.name</c> is read from it, so a shared
/// project reading its own would make both processes report as one service.
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

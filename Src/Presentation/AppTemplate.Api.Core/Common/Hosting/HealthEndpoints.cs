using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;

namespace AppTemplate.Api.Core.Common.Hosting;

/// <summary>
/// The two probes an orchestrator polls, and the one path prefix everything else agrees to ignore.
/// </summary>
/// <remarks>
/// Liveness answers "is the process up" with no dependency, so an orchestrator does not restart the
/// API because the database is briefly unreachable. Readiness answers "can it serve traffic", and
/// anything tagged <see cref="ReadyTag"/> can say no — a host's own database check, and a shutdown
/// already under way.
/// </remarks>
public static class HealthEndpoints
{
    /// <summary>
    /// Where both probes are served. Read by the tracer's filter and by the request log, which
    /// exclude these paths because they are frequent, uninteresting, and would dominate every
    /// signal.
    /// </summary>
    public const string PathPrefix = "/health";

    /// <summary>
    /// The tag that puts a check on the readiness probe rather than only on liveness. A host adds
    /// its own checks under it; <see cref="AddCoreHealthChecks"/> adds the shutdown check.
    /// </summary>
    public const string ReadyTag = "ready";

    private const string _readyPath = PathPrefix + "/ready";

    /// <summary>
    /// Registers the shutdown check and returns the builder, so a host chains whatever else it
    /// wants on the readiness probe — a <c>DbContext</c> check being the usual one.
    /// </summary>
    /// <param name="services">The container being built.</param>
    /// <returns>The health-checks builder, for the host to add its own checks to.</returns>
    public static IHealthChecksBuilder AddCoreHealthChecks(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddHealthChecks()
            .AddCheck<ShutdownHealthCheck>(name: "shutdown", tags: [ReadyTag]);
    }

    /// <summary>
    /// Maps both probes. Call it beside <c>MapControllers</c>: this is endpoint mapping, not
    /// middleware, so it deliberately sits outside the pipeline call.
    /// </summary>
    /// <param name="app">The application whose endpoints are being mapped.</param>
    /// <returns>The same application, for chaining.</returns>
    /// <remarks>
    /// Off the rate limiter, both of them: without this an orchestrator's probe shares the budget of
    /// real traffic, and behind a mesh sidecar or a host-network ingress the probe and inbound
    /// traffic can even share one source address and partition. A traffic spike then answers the
    /// probe 429 too, which the orchestrator reads as "unhealthy" on readiness and as "kill it" on
    /// liveness — right as the instance is already struggling, cascading the load onto whatever
    /// replicas survive. Anonymous for the same kind of reason: the fallback policy demands an
    /// authenticated user, and a probe holds no token.
    /// </remarks>
    public static WebApplication MapCoreHealthEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapHealthChecks(PathPrefix, new HealthCheckOptions
        {
            Predicate = _ => false,
        }).AllowAnonymous().DisableRateLimiting();

        app.MapHealthChecks(_readyPath, new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),
        }).AllowAnonymous().DisableRateLimiting();

        return app;
    }
}

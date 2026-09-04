using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AppTemplate.Api.Common.Lifecycle;

/// <summary>
/// Readiness only. Turns <see cref="HealthStatus.Unhealthy"/> the instant graceful shutdown
/// begins, so the orchestrator stops routing new traffic before Kestrel closes its listening
/// sockets — otherwise a request arriving in that window is refused at the TCP level instead of
/// drained, which is a self-inflicted burst of failed requests on every deploy.
/// <para>
/// Never wire this into liveness. Liveness answers "is the process up", with no opinion on
/// traffic; failing it during a graceful stop would ask the orchestrator to kill a process that is
/// exiting cleanly, not one that is stuck.
/// </para>
/// </summary>
public sealed class ShutdownHealthCheck(IHostApplicationLifetime lifetime) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        HealthCheckResult result = lifetime.ApplicationStopping.IsCancellationRequested
            ? HealthCheckResult.Unhealthy("Shutdown has begun; not accepting new traffic.")
            : HealthCheckResult.Healthy();

        return Task.FromResult(result);
    }
}

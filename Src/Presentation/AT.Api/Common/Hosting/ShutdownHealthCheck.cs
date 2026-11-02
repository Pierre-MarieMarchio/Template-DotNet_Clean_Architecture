using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AppTemplate.Api.Common.Hosting;

internal sealed class ShutdownHealthCheck(IHostApplicationLifetime lifetime) : IHealthCheck
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

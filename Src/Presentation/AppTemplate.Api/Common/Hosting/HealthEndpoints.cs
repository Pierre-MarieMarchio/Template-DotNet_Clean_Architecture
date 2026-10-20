using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace AppTemplate.Api.Common.Hosting;

public static class HealthEndpoints
{
    public const string PathPrefix = "/health";

    public const string ReadyTag = "ready";

    private const string _readyPath = PathPrefix + "/ready";

    public static IHealthChecksBuilder AddApiHealthChecks(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddHealthChecks()
            .AddCheck<ShutdownHealthCheck>(name: "shutdown", tags: [ReadyTag]);
    }

    public static WebApplication MapApiHealthEndpoints(this WebApplication app)
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

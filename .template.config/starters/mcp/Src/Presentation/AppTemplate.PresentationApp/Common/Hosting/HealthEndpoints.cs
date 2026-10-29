﻿using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace AppTemplate.PresentationApp.Common.Hosting;

internal static class HealthEndpoints
{
    internal const string ReadyTag = "ready";

    internal static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),
        });

        return endpoints;
    }
}

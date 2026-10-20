using AppTemplate.Api.Common.Errors;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Hosting;

internal static class HostLifecycleExtensions
{
    public static IServiceCollection AddApiLifecycle(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ShutdownOptions>()
            .Bind(configuration.GetSection(ShutdownOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<ShutdownOptions>, ShutdownOptionsValidator>();

        services.AddOptions<HostOptions>()
            .Configure<IOptions<ShutdownOptions>>(
                static (hostOptions, shutdown) => hostOptions.ShutdownTimeout = shutdown.Value.Timeout);

        services.AddOptions<RequestTimeoutsOptions>()
            .Bind(configuration.GetSection(RequestTimeoutsOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<RequestTimeoutsOptions>, RequestTimeoutsOptionsValidator>();

        services.AddRequestTimeouts();
        services.AddOptions<RequestTimeoutOptions>()
            .Configure<IOptions<RequestTimeoutsOptions>>(
                static (options, requestTimeouts) => options.DefaultPolicy = new RequestTimeoutPolicy
                {
                    Timeout = requestTimeouts.Value.Default,
                    WriteTimeoutResponse = WriteTimeoutProblemAsync,
                });

        return services;
    }

    public static WebApplication UseApiRequestTimeouts(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseRequestTimeouts();

        return app;
    }

    private static async Task WriteTimeoutProblemAsync(HttpContext httpContext)
    {
        var problem = new ProblemDetails
        {
            Status = httpContext.Response.StatusCode,
            Title = "Request timeout",
            Detail = "The server did not complete the request within its configured timeout.",
        };

        problem.Extensions["code"] = "request.timeout";
        ProblemDetailsNormaliser.Normalise(problem, httpContext);

        await httpContext.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            CancellationToken.None);
    }
}

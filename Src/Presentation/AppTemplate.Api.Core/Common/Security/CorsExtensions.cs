using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using FrameworkCorsOptions = Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions;

namespace AppTemplate.Api.Core.Common.Security;

/// <summary>
/// CORS driven by configuration, so the allowed origins differ per environment without a rebuild.
/// </summary>
internal static class CorsExtensions
{
    public const string Default = "default";

    public static IServiceCollection AddApiCors(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<CorsOptions>()
            .Bind(configuration.GetSection(CorsOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<CorsOptions>, CorsOptionsValidator>();

        services.AddCors();
        services.AddOptions<FrameworkCorsOptions>().Configure<IOptions<CorsOptions>>(
            static (cors, apiCors) => cors.AddPolicy(Default, policy =>
            {
                var allowedOrigins = apiCors.Value.AllowedOrigins;

                if (allowedOrigins.Count == 0)
                {
                    // Nothing configured means allow nothing. CORS governs only cross-origin
                    // requests, so same-origin callers are unaffected.
                    return;
                }

                policy.WithOrigins([.. allowedOrigins])
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    // A browser hands script only the CORS-safelisted response headers, so a header a
                    // client must act on reads as absent unless it is named here.
                    .WithExposedHeaders("Retry-After", "ETag", "Location", "Idempotency-Replayed")
                    .SetPreflightMaxAge(TimeSpan.FromMinutes(10));

                // No AllowCredentials: tokens travel in the Authorization header, not a cookie.
            }));

        return services;
    }
}

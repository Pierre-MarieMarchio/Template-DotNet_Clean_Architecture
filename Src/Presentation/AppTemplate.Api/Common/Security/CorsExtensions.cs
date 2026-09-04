namespace AppTemplate.Api.Common.Security;

/// <summary>
/// CORS driven by configuration, so the allowed origins differ per environment without a rebuild.
/// </summary>
public static class CorsExtensions
{
    public const string Default = "default";

    public const string AllowedOriginsKey = "Cors:AllowedOrigins";

    public static IServiceCollection AddApiCors(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        string[] allowedOrigins = configuration.GetSection(AllowedOriginsKey).Get<string[]>() ?? [];

        services.AddCors(options => options.AddPolicy(Default, policy =>
        {
            if (allowedOrigins.Length == 0)
            {
                // Nothing configured means allow nothing, not allow everything. Same-origin callers
                // are unaffected: CORS only governs cross-origin requests.
                return;
            }

            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                // Without this a browser cannot read Retry-After off a cross-origin 429.
                .WithExposedHeaders("Retry-After")
                .SetPreflightMaxAge(TimeSpan.FromMinutes(10));

            // AllowCredentials is deliberately not set: tokens travel in the Authorization header,
            // not a cookie, and it is that combination that turns a permissive policy into a hole.
        }));

        return services;
    }
}

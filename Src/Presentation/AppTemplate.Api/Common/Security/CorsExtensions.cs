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
                // A browser hands script only the CORS-safelisted response headers unless the
                // server names the rest, so every header this API expects a client to act on has
                // to be listed here or it reads as absent. Retry-After for a 429; ETag because
                // If-Match is how every conditional write in this template is made, and a client
                // that cannot read one cannot send one back; Location for the 201s; and
                // Idempotency-Replayed so a caller retrying a POST can tell a stored answer from
                // a fresh one, which is the whole point of having sent the key.
                .WithExposedHeaders("Retry-After", "ETag", "Location", "Idempotency-Replayed")
                .SetPreflightMaxAge(TimeSpan.FromMinutes(10));

            // AllowCredentials is deliberately not set: tokens travel in the Authorization header,
            // not a cookie, and it is that combination that turns a permissive policy into a hole.
        }));

        return services;
    }
}

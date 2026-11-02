using Microsoft.Extensions.Options;
using FrameworkCorsOptions = Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions;

namespace AppTemplate.Api.Common.Security;

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
                    return;
                }

                policy.WithOrigins([.. allowedOrigins])
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .WithExposedHeaders("Retry-After", "ETag", "Location", "Idempotency-Replayed")
                    .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
            }));

        return services;
    }
}

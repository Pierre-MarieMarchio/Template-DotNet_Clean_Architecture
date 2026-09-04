using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Idempotency;

public static class IdempotencyExtensions
{
    public static IServiceCollection AddApiIdempotency(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<IdempotencyOptions>()
            .Bind(configuration.GetSection(IdempotencyOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<IdempotencyOptions>, IdempotencyOptionsValidator>();

        return services;
    }
}

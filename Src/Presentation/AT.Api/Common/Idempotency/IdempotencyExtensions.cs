using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Idempotency;

internal static class IdempotencyExtensions
{
    internal static IServiceCollection AddApiIdempotency(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<IdempotencyOptions>()
            .Bind(configuration.GetSection(IdempotencyOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<IdempotencyOptions>, IdempotencyOptionsValidator>();

        services.Configure<MvcOptions>(options => options.Filters.Add<IdempotencyFilter>());

        return services;
    }
}

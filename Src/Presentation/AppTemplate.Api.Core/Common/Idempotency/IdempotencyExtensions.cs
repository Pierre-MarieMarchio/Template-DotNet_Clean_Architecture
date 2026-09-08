using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Core.Common.Idempotency;

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

        // Global rather than per-controller, and registered here rather than in a host's
        // AddControllers call: the filter is inert on any action without [Idempotent], so this is
        // safe, and it is one fewer thing every host and every controller has to remember.
        services.Configure<MvcOptions>(options => options.Filters.Add<IdempotencyFilter>());

        return services;
    }
}

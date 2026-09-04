using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Concurrency;

public static class ConcurrencyExtensions
{
    public static IServiceCollection AddApiConcurrency(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ConcurrencyOptions>()
            .Bind(configuration.GetSection(ConcurrencyOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<ConcurrencyOptions>, ConcurrencyOptionsValidator>();

        return services;
    }
}

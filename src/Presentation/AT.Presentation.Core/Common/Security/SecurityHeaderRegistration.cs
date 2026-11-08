using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AppTemplate.Presentation.Core.Common.Security;

public static class SecurityHeaderRegistration
{
    public static IServiceCollection AddSecurityHeaderOptions(
        this IServiceCollection services,
        IConfiguration configuration,
        string defaultContentSecurityPolicy = SecurityHeaderOptions.DefaultContentSecurityPolicy)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultContentSecurityPolicy);

        services.AddOptions<SecurityHeaderOptions>()
            .Configure(options => options.ContentSecurityPolicy = defaultContentSecurityPolicy)
            .Bind(configuration.GetSection(SecurityHeaderOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<SecurityHeaderOptions>, SecurityHeaderOptionsValidator>();

        return services;
    }
}

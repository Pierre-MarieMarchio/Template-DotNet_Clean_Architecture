using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Presentation.Core.Common.Localization;
using AppTemplate.Presentation.Core.Common.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AppTemplate.Presentation.Core;

public static class PresentationCoreModule
{
    public static IServiceCollection AddLocalizationOptions(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<LocalizationOptions>()
            .BindConfiguration(LocalizationOptions.SectionName)
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<LocalizationOptions>, LocalizationOptionsValidator>();

        return services;
    }

    public static IServiceCollection AddNoCallerIdentity(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ICurrentUser, NoCallerCurrentUser>();

        return services;
    }
}

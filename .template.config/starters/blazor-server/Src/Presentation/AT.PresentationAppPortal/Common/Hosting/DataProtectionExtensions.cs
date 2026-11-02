using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace AppTemplate.PresentationAppPortal.Common.Hosting;

internal static class DataProtectionExtensions
{
    internal static IServiceCollection AddDataProtectionKeys(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<DataProtectionOptions>()
            .Bind(configuration.GetSection(DataProtectionOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<DataProtectionOptions>, DataProtectionOptionsValidator>();

        var dataProtection = services.AddDataProtection().SetApplicationName("AppTemplate.PresentationAppPortal");

        if (configuration.GetSection(DataProtectionOptions.SectionName).Get<DataProtectionOptions>()?.KeysDirectory is { Length: > 0 } keys)
        {
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keys));
        }

        return services;
    }
}

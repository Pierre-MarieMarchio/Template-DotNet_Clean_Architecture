using Microsoft.Extensions.Options;

namespace AppTemplate.PresentationAppMcp.Common.Hosting;

internal static class McpOptionsExtensions
{
    internal static IServiceCollection AddMcpOptions(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<McpOptions>()
            .Bind(configuration.GetSection(McpOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<McpOptions>, McpOptionsValidator>();

        return services;
    }
}

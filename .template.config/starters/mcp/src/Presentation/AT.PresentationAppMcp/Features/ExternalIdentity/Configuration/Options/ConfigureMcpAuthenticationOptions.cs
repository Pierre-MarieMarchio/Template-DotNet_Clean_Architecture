using AppTemplate.Presentation.Core.Common.Security;
using AppTemplate.PresentationAppMcp.Common.Hosting;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore.Authentication;

namespace AppTemplate.PresentationAppMcp.Features.ExternalIdentity.Configuration.Options;

internal sealed class ConfigureMcpAuthenticationOptions(
    IOptions<AuthenticationExternalOptions> externalOptions,
    IOptions<McpOptions> mcpOptions) : IConfigureNamedOptions<McpAuthenticationOptions>
{
    public void Configure(McpAuthenticationOptions options) =>
        Configure(McpAuthenticationDefaults.AuthenticationScheme, options);

    public void Configure(string? name, McpAuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!string.Equals(name, McpAuthenticationDefaults.AuthenticationScheme, StringComparison.Ordinal))
        {
            return;
        }

        options.ResourceMetadata = new()
        {
            Resource = mcpOptions.Value.Resource,
            AuthorizationServers = { externalOptions.Value.Authority },
        };
    }
}

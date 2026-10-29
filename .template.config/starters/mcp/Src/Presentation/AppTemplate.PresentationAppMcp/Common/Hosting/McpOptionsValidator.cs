using Microsoft.Extensions.Options;

namespace AppTemplate.PresentationAppMcp.Common.Hosting;

internal sealed class McpOptionsValidator : IValidateOptions<McpOptions>
{
    public ValidateOptionsResult Validate(string? name, McpOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!Uri.TryCreate(options.Resource, UriKind.Absolute, out var resource)
            || resource.Scheme is not ("https" or "http")
            || !string.IsNullOrEmpty(resource.Query)
            || !string.IsNullOrEmpty(resource.Fragment)
            || !resource.AbsolutePath.EndsWith(McpEndpoint.Path, StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Fail(
                $"{McpOptions.SectionName}:Resource must be the absolute http or https address MCP clients connect to, "
                + $"ending in {McpEndpoint.Path}, without a query or a fragment: tokens are issued for it.");
        }

        return ValidateOptionsResult.Success;
    }
}

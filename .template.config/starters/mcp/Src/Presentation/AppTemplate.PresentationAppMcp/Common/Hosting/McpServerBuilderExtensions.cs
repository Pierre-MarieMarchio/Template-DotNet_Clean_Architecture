using ModelContextProtocol.Server;

namespace AppTemplate.PresentationAppMcp.Common.Hosting;

internal static class McpServerBuilderExtensions
{
    internal static IMcpServerBuilder WithToolsListedEvenWhenNone(this IMcpServerBuilder server)
    {
        ArgumentNullException.ThrowIfNull(server);

        server.Services.PostConfigure<McpServerOptions>(options => options.ToolCollection ??= []);

        return server;
    }
}

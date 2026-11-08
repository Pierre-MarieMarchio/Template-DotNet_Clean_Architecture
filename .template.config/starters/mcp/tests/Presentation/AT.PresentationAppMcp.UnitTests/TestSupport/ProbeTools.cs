using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.PresentationAppMcp.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace AppTemplate.PresentationAppMcp.UnitTests.TestSupport;

[McpServerToolType]
internal sealed class ProbeTools(ICurrentUser caller)
{
    internal const string Reads = "probe_who_am_i";

    internal const string Writes = "probe_write";

    internal static void Offer(IServiceCollection services) => services.AddMcpServer().WithTools<ProbeTools>();

    [McpServerTool(Name = Reads, ReadOnly = true)]
    [Authorize(Policy = ToolPolicies.Read)]
    public string WhoAmI() => caller.UserId?.Value.ToString() ?? string.Empty;

    [McpServerTool(Name = Writes, Destructive = false)]
    [Authorize(Policy = ToolPolicies.Write)]
    public static string Write() => "written";
}

using System.Globalization;
using CleanArch.Tooling.Projects;

namespace CleanArch.Cli.Joins;

public sealed record WebJoinOptions(string? Framework = null, string? Audience = null, string? Rendering = null, string? Port = null)
{
    public IReadOnlyList<string> Given =>
        [.. new[] { ("--web", Framework), ("--web-audience", Audience), ("--web-rendering", Rendering), ("--web-port", Port) }
            .Where(option => option.Item2 is not null)
            .Select(option => option.Item1)];
}

internal static class WebJoin
{
    private const string _serverRenderingDecision = "0024-a-server-rendered-web-app-runs-on-its-own-server.md";

    private const string _workspaceReadme = "Src/Web/README.md";

    public static int Run(LocatedProject project, string name, WebJoinOptions options, TextWriter output, TextWriter error) =>
        JoinClosing.Refused(error, () => Join(project, name, options, output));

    private static void Join(LocatedProject project, string name, WebJoinOptions options, TextWriter output)
    {
        string app = Path.Combine(project.Root, "Src", "Web", "apps", name);
        if (!Directory.Exists(app) && Directory.Exists(Path.Combine(project.Root, "Src", "Web", "packages", name)))
        {
            JoinPackage(project, name, options, output);
            return;
        }

        WebApp joined = Described(name, options) with { Port = Port(app, name, options.Port) };
        string scope = Scope(project.Root);
        string folder = "Src/Web/apps/" + name;

        ItemJoin.Run(project.Root, new JoinedItem(folder, "cleanarch-webapp", "web-scaffold", name, Required: false, FirstAppsWorkspaceReadme(project)), output);
        PlaceTheServerRenderingDecision(project.Root, folder, output);
        JoinClosing.Indexed(project, output);
        ProjectRecord record = ProjectRecord.Read(project.Root, output);
        record.RecordWebApp(joined, scope);
        JoinClosing.RecordClient(project, record);
    }

    private static void JoinPackage(LocatedProject project, string name, WebJoinOptions options, TextWriter output)
    {
        if (options.Given.Count > 0)
        {
            throw new JoinRefusedException($"Src/Web/packages/{name} is a package: {string.Join(", ", options.Given)} belong to an app.");
        }

        ProjectRecord.Read(project.Root, output).RecordWebPackage(name, Scope(project.Root));
    }

    private static WebApp Described(string name, WebJoinOptions options)
    {
        string framework = options.Framework
            ?? throw new JoinRefusedException(
                $"The 'web-join' task records the app in {ProjectLocator.FileName} with the --framework cleanarch-webapp was given: "
                + $"dotnet run Tools/Tasks.cs web-join {name} --web <framework>, with --web-audience and --web-rendering when its "
                + "--audience and --rendering were not public and spa, and --web-port when it was given a --port.");
        string audience = options.Audience ?? "public";
        string rendering = options.Rendering ?? "spa";

        return audience is "public" or "admin" && rendering is "spa" or "ssr"
            ? new WebApp(name, framework, audience, rendering)
            : throw new JoinRefusedException($"--web-audience is public or admin, and --web-rendering spa or ssr; not '{audience}' and '{rendering}'.");
    }

    internal static int Port(string app, string name, string? given)
    {
        int port = GeneratedProjectFacts.WebPort(app)
            ?? throw new JoinRefusedException($"Src/Web/apps/{name}/compose.yml holds no WEB_<NAME>_HOST_PORT default, so the app's port is not known.");

        return given is null || (int.TryParse(given, NumberStyles.None, CultureInfo.InvariantCulture, out int asked) && asked == port)
            ? port
            : throw new JoinRefusedException($"--web-port {given} is not the port Src/Web/apps/{name}/compose.yml holds, {port}: give cleanarch-webapp's --port.");
    }

    private static string Scope(string root) =>
        GeneratedProject.WorkspaceScope(Path.Combine(root, "Src", "Web", "package.json"))
        ?? throw new JoinRefusedException("Src/Web/package.json names no @<scope>/ package, so the workspace's scope cannot be recorded.");

    private static string? FirstAppsWorkspaceReadme(LocatedProject project) =>
        project.File.Web is null && ReadmeEdits.Lists(Path.Combine(project.Root, _workspaceReadme)) ? _workspaceReadme : null;

    private static void PlaceTheServerRenderingDecision(string root, string app, TextWriter output)
    {
        string brought = Path.Combine(root, app, _serverRenderingDecision);
        string decision = Path.Combine(root, "docs", "decisions", _serverRenderingDecision);

        if (!File.Exists(brought))
        {
            return;
        }

        if (File.Exists(decision))
        {
            File.Delete(brought);
            output.WriteLine($"In place already: docs/decisions/{_serverRenderingDecision}");
            return;
        }

        File.Move(brought, decision);
        output.WriteLine($"Moved: {app}/{_serverRenderingDecision} to docs/decisions/{_serverRenderingDecision}");
    }
}

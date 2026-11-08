using System.Globalization;
using CleanArch.Tooling.Additions;
using CleanArch.Tooling.Projects;

namespace CleanArch.Cli.Joins;

internal static class WebJoin
{
    private const string _workspaceReadme = "src/Web/README.md";

    public static int Run(LocatedProject project, string name, WebJoinOptions options, TextWriter output, TextWriter error) =>
        JoinClosing.Refused(error, () => Join(project, name, options, output));

    private static void Join(LocatedProject project, string name, WebJoinOptions options, TextWriter output)
    {
        string app = Path.Combine(project.Root, "src", "Web", "apps", name);
        if (!Directory.Exists(app) && Directory.Exists(Path.Combine(project.Root, "src", "Web", "packages", name)))
        {
            JoinPackage(project, name, options, output);
            return;
        }

        WebApp joined = Described(name, options) with { Port = Port(app, name, options.Port) };
        string scope = Scope(project.Root);
        string folder = "src/Web/apps/" + name;

        ItemJoin.Run(project.Root, new JoinedItem(folder, "cleanarch-webapp", Required: false, FirstAppsWorkspaceReadme(project)), output);
        JoinClosing.Indexed(project, output);
        ProjectRecord record = ProjectRecord.Read(project.Root, output);
        record.RecordWebApp(joined, scope);
        JoinClosing.RecordClient(project, record);
    }

    private static void JoinPackage(LocatedProject project, string name, WebJoinOptions options, TextWriter output)
    {
        if (options.Given.Count > 0)
        {
            throw new JoinRefusedException($"src/Web/packages/{name} is a package: {string.Join(", ", options.Given)} belong to an app.");
        }

        ProjectRecord.Read(project.Root, output).RecordWebPackage(name, Scope(project.Root));
    }

    private static WebApp Described(string name, WebJoinOptions options)
    {
        string framework = options.Framework
            ?? throw new JoinRefusedException(
                $"src/Web/apps/{name} is recorded in {ProjectLocator.FileName} with the --framework cleanarch-webapp was given: "
                + $"{Tool.Name} add web-app --name {name} --framework <framework>, with --audience and --rendering when they "
                + "were not public and spa, and --port when one was given.");
        string audience = options.Audience ?? "public";
        string rendering = options.Rendering ?? "spa";

        return audience is "public" or "admin" && rendering is "spa" or "ssr"
            ? new WebApp(name, framework, audience, rendering)
            : throw new JoinRefusedException($"--audience is public or admin, and --rendering spa or ssr; not '{audience}' and '{rendering}'.");
    }

    internal static int Port(string app, string name, string? given)
    {
        int port = GeneratedProjectFacts.WebPort(app)
            ?? throw new JoinRefusedException($"src/Web/apps/{name}/compose.yml holds no WEB_<NAME>_HOST_PORT default, so the app's port is not known.");

        return given is null || (int.TryParse(given, NumberStyles.None, CultureInfo.InvariantCulture, out int asked) && asked == port)
            ? port
            : throw new JoinRefusedException($"--port {given} is not the port src/Web/apps/{name}/compose.yml holds, {port}: give the --port cleanarch-webapp was given.");
    }

    private static string Scope(string root) =>
        GeneratedProject.WorkspaceScope(Path.Combine(root, "src", "Web", "package.json"))
        ?? throw new JoinRefusedException("src/Web/package.json names no @<scope>/ package, so the workspace's scope cannot be recorded.");

    private static string? FirstAppsWorkspaceReadme(LocatedProject project) =>
        project.File.Web is null && ReadmeEdits.Lists(Path.Combine(project.Root, _workspaceReadme)) ? _workspaceReadme : null;
}

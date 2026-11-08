namespace CleanArch.Cli.Commands.Kernel;

internal static class Scaffolds
{
    private const string _workspaceHelp =
        "The web workspace needs Node.js and pnpm: run `corepack enable` once, then `pnpm install` and `pnpm api` in src/Web.";

    public static int Presentations(LocatedProject project, Shell shell, string? only) =>
        TestRuns.Guarded(shell, () => WriteTheClientWhereJoined(project, shell, CreatePresentations(project, shell, only)));

    public static int WebApps(LocatedProject project, Shell shell, string? only) =>
        TestRuns.Guarded(shell, () => WriteTheClientWhereJoined(project, shell, CreateWebApps(project, shell, only)));

    public static void Required(Shell shell, ExternalCall call, string? why = null)
    {
        int exit = shell.Run(call);
        if (exit != ExitCode.Succeeded)
        {
            throw new InvalidOperationException($"'{call.Line}' exited with {exit}." + (why is null ? string.Empty : " " + why));
        }
    }

    private static bool CreatePresentations(LocatedProject project, Shell shell, string? only)
    {
        string presentation = Path.Combine(project.Root, "src", "Presentation");
        string[] pending = Pending(presentation, _ => true);
        if (only is not null)
        {
            string folder = Path.Combine(presentation, ProjectScaffold.FolderOfProject(project, only));
            pending = Only(shell, pending, folder, ProjectPath.Relative(project.Root, folder), File.Exists(Path.Combine(folder, only + ".csproj")), string.Empty);
        }

        bool[] joined = [.. pending.Select(folder => ProjectScaffold.Create(project, shell, folder))];
        return joined.Contains(true);
    }

    private static bool CreateWebApps(LocatedProject project, Shell shell, string? only)
    {
        string web = Path.Combine(project.Root, ProjectLayout.Web);
        if (!File.Exists(Path.Combine(web, "package.json")))
        {
            throw new InvalidOperationException(
                "src/Web holds no package.json, so there is no web workspace to create an app in. "
                + "dotnet new cleanarch-webapp --workspace adds a first app and the workspace around it.");
        }

        string apps = Path.Combine(web, "apps");
        string[] pending = Pending(apps, folder => !File.Exists(Path.Combine(folder, "package.json")));
        if (only is not null)
        {
            string folder = Path.Combine(apps, only);
            bool created = File.Exists(Path.Combine(folder, "package.json"))
                || (Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*.csproj").Any());
            pending = Only(shell, pending, folder, $"src/Web/apps/{only}", created, " dotnet new cleanarch-webapp writes one in the app's folder.");
        }

        bool[] joined = [.. pending.Select(folder => CreateWebApp(project, shell, web, folder))];
        Install(project, shell, pending.Length > 0);
        if (pending.Length > 0)
        {
            shell.Output.WriteLine(
                $"Created {string.Join(", ", pending.Select(Path.GetFileName))}. Commit the app with src/Web/pnpm-lock.yaml: "
                + "pnpm install --frozen-lockfile, CI and the image build read the versions it records.");
        }

        return joined.Contains(true);
    }

    private static bool CreateWebApp(LocatedProject project, Shell shell, string web, string folder)
    {
        if (ScaffoldDescriptor.Read(folder).Kind == "dotnet")
        {
            return ProjectScaffold.Create(project, shell, folder);
        }

        WebAppScaffold.Create(shell, web, folder);
        return false;
    }

    private static void Install(LocatedProject project, Shell shell, bool created)
    {
        Workspace(project, shell, created ? ["install", "--no-frozen-lockfile"] : ["install"]);
        string apps = Path.Combine(project.Root, ProjectLayout.Web, "apps");
        string[] installed = Directory.Exists(apps)
            ? [.. Directory.GetDirectories(apps).Where(folder => File.Exists(Path.Combine(folder, "package.json")))]
            : [];
        bool[] pinned = [.. installed.Select(WebAppScaffold.PinInstalledVersions)];
        if (pinned.Contains(true))
        {
            Workspace(project, shell, ["install", "--no-frozen-lockfile"]);
        }

        Workspace(project, shell, ["api"]);
    }

    private static void Workspace(LocatedProject project, Shell shell, IReadOnlyList<string> arguments) =>
        Required(shell, Pnpm.In(project, ProjectLayout.Web, arguments), _workspaceHelp);

    private static string[] Pending(string parent, Func<string, bool> uncreated) =>
        Directory.Exists(parent)
            ? [.. Directory.GetDirectories(parent).Where(folder => ScaffoldDescriptor.In(folder) && uncreated(folder)).Order(StringComparer.Ordinal)]
            : [];

    private static string[] Only(Shell shell, string[] pending, string folder, string shown, bool created, string howToGetOne)
    {
        if (pending.Contains(folder, StringComparer.Ordinal))
        {
            return [folder];
        }

        if (!created)
        {
            throw new InvalidOperationException($"{shown} holds no {ScaffoldDescriptor.FileName}, so there is nothing to create.{howToGetOne}");
        }

        shell.Output.WriteLine($"{shown} holds no {ScaffoldDescriptor.FileName}: it is created already, and left alone.");
        return [];
    }

    private static int WriteTheClientWhereJoined(LocatedProject project, Shell shell, bool joined)
    {
        if (!joined || !File.Exists(OpenApiReference.PathOf(project)))
        {
            return ExitCode.Succeeded;
        }

        Required(shell, Dotnet.Call(project.Root, "tool", "restore"));
        return ApiClient.Write(project, shell);
    }
}

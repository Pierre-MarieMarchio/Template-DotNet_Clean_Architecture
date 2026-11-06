using System.CommandLine;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using CleanArch.Cli.Commands.Bootstrap;
using CleanArch.Cli.Commands.Format;
using CleanArch.Cli.Commands.Hosting;
using CleanArch.Cli.Commands.Kernel;
using CleanArch.Cli.Commands.OpenApi;
using CleanArch.Cli.Commands.Test;

namespace CleanArch.Cli.Commands.Verify;

internal static class VerifyCommand
{
    public const string Name = "verify";

    public static readonly CommandRegistration Registration = new(Refusal, Build);

    private static readonly string _pnpm = OperatingSystem.IsWindows() ? "pnpm.cmd" : "pnpm";

    private static readonly string[] _selfTestedGates =
    [
        "Tasks.cs",
        "NewFeature.cs",
        "NewUseCase.cs",
        "NewSetting.cs",
        "NewEvent.cs",
        "RemoveFeature.cs",
        "UpgradeReport.cs",
    ];

    private static readonly string[] _webScripts = ["lint", "test", "build"];

    private const string _webNote = "Src/Web";

    private const string _apiClientPackageNote = "Src/Web/packages/api-client";

    private const string _vulnerableMarker = "has the following vulnerable packages";

    private static string? Refusal(CommandContext context) =>
        context.Project is null ? context.RefusedProjectFile?.Message ?? context.OutsideProject : null;

    private static Command Build(CommandContext context)
    {
        var verify = new Command(
            Name,
            "Run this project's whole gate: the repository checks, the client and web checks its project file makes true, "
            + "the build, every test, the pending-model-changes checks and the vulnerable-packages audit.");
        BuildSettings.Options settings = BuildSettings.Add(verify);
        if (context.Project is { } project)
        {
            verify.SetAction(parseResult => Run(project, context.Shell(parseResult), settings.Of(parseResult)));
        }

        return verify;
    }

    private static int Run(LocatedProject project, Shell shell, BuildSettings settings)
    {
        if (MissingInitialMigration(project) is { } missing)
        {
            shell.Error.WriteLine(
                $"'verify' needs the initial migration of {missing}, and there is none yet. "
                + $"Run `{CleanArchCommand.Name} {BootstrapCommand.Name}`, which creates it, then commit it (docs/guides/migrations.md).");
            return CleanArchCommand.Failed;
        }

        foreach (Func<int> step in Steps(project, settings, shell))
        {
            int exit = step();
            if (exit != CleanArchCommand.Succeeded)
            {
                return exit;
            }
        }

        return CleanArchCommand.Succeeded;
    }

    private static IEnumerable<Func<int>> Steps(LocatedProject project, BuildSettings settings, Shell shell)
    {
        foreach (ExternalCall call in GateCalls(project))
        {
            yield return () => shell.Run(call);
        }

        if (project.File.Web is not null)
        {
            yield return () => shell.Run(Pnpm(["--version"], project.Root));
        }

        if (project.File.Client)
        {
            yield return () => ApiClientCheck(project, shell);
        }

        if (project.File.Web is not null)
        {
            yield return () => WebSchema(project, shell);
            yield return () => shell.Run(Pnpm(["audit"], WebFolder(project), _webNote));
            yield return () => shell.Run(Pnpm(["install", "--frozen-lockfile"], WebFolder(project), _webNote));
            foreach (string script in _webScripts)
            {
                yield return () => shell.Run(Pnpm([script], WebFolder(project), _webNote));
            }
        }

        if (TemplateMaintenance(project))
        {
            yield return () => shell.Run(Dotnet(["run", Tool("Tasks.cs"), "web-lockfiles", "--check"], project.Root));
        }

        string solution = ProjectLayout.Solution(project);
        yield return () => shell.Run(Dotnet(["restore", solution, .. settings.Nodes], project.Root));
        yield return () => shell.Run(Dotnet(["format", solution, "--diagnostics", .. FormatCommand.StyleRulesTheBuildLacks, "--verify-no-changes", "--no-restore"], project.Root));
        yield return () => shell.Run(Dotnet(["build", solution, "--configuration", settings.Configuration, "--no-restore", .. settings.Nodes], project.Root));
        yield return () => TestCommand.RunBuilt(project, shell, settings);

        foreach (string context in ProjectLayout.Contexts(project))
        {
            yield return () => shell.Run(Dotnet(["ef", "migrations", "has-pending-model-changes", "--project", context, "--startup-project", context, "--no-build"], project.Root));
        }

        yield return () => VulnerablePackages(project, solution, shell);

        if (LauncherSolution(project) is { } launcher)
        {
            yield return () => shell.Run(Dotnet(["restore", launcher], project.Root));
            yield return () => shell.Run(Dotnet(["format", launcher, "--diagnostics", .. FormatCommand.StyleRulesTheBuildLacks, "--verify-no-changes", "--no-restore"], project.Root));
            yield return () => shell.Run(Dotnet(["build", launcher, "--configuration", settings.Configuration, "--no-restore"], project.Root));
            yield return () => shell.Run(Dotnet(["test", "--solution", launcher, "--configuration", settings.Configuration, "--no-build"], project.Root));
        }
    }

    private static IEnumerable<ExternalCall> GateCalls(LocatedProject project)
    {
        foreach (string gate in _selfTestedGates)
        {
            yield return Dotnet(["run", Tool(gate), "--self-test"], project.Root);
        }

        foreach (ExternalCall call in OptionalGate(project, "CheckDocPaths.cs", "--self-test"))
        {
            yield return call;
        }

        foreach (ExternalCall call in OptionalGate(project, "CheckDocPaths.cs", project.Root))
        {
            yield return call;
        }

        foreach (ExternalCall call in OptionalGate(project, "CheckWorkflows.cs", "--self-test"))
        {
            yield return call;
        }

        foreach (ExternalCall call in OptionalGate(project, "CheckWorkflows.cs", project.Root))
        {
            yield return call;
        }

        yield return Dotnet(["run", Tool("CoverageGate.cs"), "--self-test"], project.Root);

        foreach (ExternalCall call in OptionalGate(project, "CheckComments.cs", "--self-test"))
        {
            yield return call;
        }

        foreach (ExternalCall call in OptionalGate(project, "CheckComments.cs", project.Root))
        {
            yield return call;
        }

        yield return Dotnet(["run", Tool("Tasks.cs"), "docs-index", "--check"], project.Root);
    }

    private static IEnumerable<ExternalCall> OptionalGate(LocatedProject project, string fileName, params string[] arguments)
    {
        if (File.Exists(Path.Combine(project.Root, "Tools", fileName)))
        {
            yield return Dotnet(["run", Tool(fileName), .. arguments], project.Root);
        }
    }

    private static int WebSchema(LocatedProject project, Shell shell)
    {
        TextWriter output = shell.Output;
        TextWriter error = shell.Error;
        string root = project.Root;
        if (!File.Exists(Path.Combine(root, "Src", "Web", "packages", "api-client", "scripts", "generate.mjs")))
        {
            output.WriteLine("Src/Web/packages/api-client/scripts/generate.mjs is missing, so the web schema is not checked.");
            return CleanArchCommand.Succeeded;
        }

        string reference = ProjectLayout.OpenApiReference(project);
        if (!File.Exists(Path.Combine(root, reference)))
        {
            error.WriteLine($"{reference.Replace('\\', '/')} is missing: run `{CleanArchCommand.Name} {OpenApiCommand.Name}` first.");
            return CleanArchCommand.Failed;
        }

        int installed = shell.Run(Pnpm(["install", "--frozen-lockfile"], WebFolder(project), _webNote));
        if (installed != CleanArchCommand.Succeeded)
        {
            return installed;
        }

        string package = Path.Combine(root, "Src", "Web", "packages", "api-client");
        return shell.Run(Pnpm(["exec", "node", "scripts/generate.mjs", "--check", reference], package, _apiClientPackageNote));
    }

    private static int ApiClientCheck(LocatedProject project, Shell shell)
    {
        TextWriter output = shell.Output;
        TextWriter error = shell.Error;
        string root = project.Root;
        string reference = ProjectLayout.OpenApiReference(project);
        string client = ProjectLayout.Client(project);
        string generated = Path.Combine(client, "Generated");

        if (!Directory.Exists(Path.Combine(root, client)))
        {
            error.WriteLine($"{client.Replace('\\', '/')} does not exist, so there is no client to check: a presentation that calls the API through it brings it.");
            return CleanArchCommand.Failed;
        }

        if (!File.Exists(Path.Combine(root, reference)))
        {
            error.WriteLine($"{reference.Replace('\\', '/')} is missing: run `{CleanArchCommand.Name} {OpenApiCommand.Name}` first.");
            return CleanArchCommand.Failed;
        }

        string written = Path.Combine(Path.GetTempPath(), "api-client-" + Guid.NewGuid().ToString("N"));
        try
        {
            int exit = shell.Run(Dotnet(
                    [
                        "tool", "run", "kiota", "generate",
                        "--language", "CSharp",
                        "--openapi", reference,
                        "--output", written,
                        "--class-name", "ApiClient",
                        "--namespace-name", $"{project.File.Name}.Client.Generated",
                        "--exclude-backward-compatible",
                        "--clean-output",
                        "--log-level", "Warning",
                    ],
                    root));
            if (exit != CleanArchCommand.Succeeded)
            {
                return exit;
            }

            if (!Directory.Exists(written))
            {
                error.WriteLine($"kiota wrote nothing to {written}.");
                return CleanArchCommand.Failed;
            }

            Normalise(written);
            RewriteLockDescription(written, Path.Combine(root, generated), Path.Combine(root, reference));

            string[] drifted = TreeDifferences(Path.Combine(root, generated), written);
            if (drifted.Length > 0)
            {
                error.WriteLine(
                    $"{generated.Replace('\\', '/')} differs from what Kiota writes from the OpenAPI reference:" + Environment.NewLine
                    + string.Join(Environment.NewLine, drifted.Select(path => "  " + path)) + Environment.NewLine
                    + $"Run `{CleanArchCommand.Name} {OpenApiCommand.Name}` and commit what it writes.");
                return CleanArchCommand.Failed;
            }

            return CleanArchCommand.Succeeded;
        }
        finally
        {
            if (Directory.Exists(written))
            {
                Directory.Delete(written, recursive: true);
            }
        }
    }

    private static void Normalise(string folder)
    {
        string log = Path.Combine(folder, ".kiota.log");
        if (File.Exists(log))
        {
            File.Delete(log);
        }

        foreach (string file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal).TrimStart('\uFEFF');
            File.WriteAllText(file, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
    }

    private static void RewriteLockDescription(string written, string generated, string reference)
    {
        string lockFile = Path.Combine(written, "kiota-lock.json");
        if (!File.Exists(lockFile))
        {
            return;
        }

        JsonNode settings = JsonNode.Parse(File.ReadAllText(lockFile))!;
        settings["descriptionLocation"] = Path.GetRelativePath(generated, reference).Replace('\\', '/');
        File.WriteAllText(
            lockFile,
            settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, NewLine = "\n" }) + "\n");
    }

    private static string[] TreeDifferences(string committed, string written)
    {
        string[] Files(string root) => Directory.Exists(root)
            ? [.. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))]
            : [];

        return [.. Files(committed)
            .Union(Files(written), StringComparer.Ordinal)
            .Where(path =>
            {
                string left = Path.Combine(committed, path);
                string right = Path.Combine(written, path);
                return !File.Exists(left) || !File.Exists(right) || !File.ReadAllBytes(left).AsSpan().SequenceEqual(File.ReadAllBytes(right));
            })
            .Order(StringComparer.Ordinal)];
    }

    private static int VulnerablePackages(LocatedProject project, string solution, Shell shell)
    {
        TextWriter output = shell.Output;
        TextWriter error = shell.Error;
        var listed = new StringWriter();
        int exit = shell.Run(
            Dotnet(["list", solution, "package", "--vulnerable", "--include-transitive"], project.Root),
            new TeeWriter(output, listed),
            error);
        if (exit != CleanArchCommand.Succeeded)
        {
            return exit;
        }

        if (listed.ToString().Contains(_vulnerableMarker, StringComparison.OrdinalIgnoreCase))
        {
            error.WriteLine(
                "A package with a known vulnerability is referenced, directly or transitively. The listing above names it. "
                + "Pin a patched version in Directory.Packages.props, under a Security pins label, with a comment saying when the pin can be removed.");
            return CleanArchCommand.Failed;
        }

        return CleanArchCommand.Succeeded;
    }

    private static string? MissingInitialMigration(LocatedProject project)
    {
        string[] missing = [.. ProjectLayout.Contexts(project)
            .Where(context => !HasMigrations(Path.Combine(project.Root, context)))
            .Select(Path.GetFileName)
            .OfType<string>()];

        return missing.Length > 0 ? string.Join(" and ", missing) : null;
    }

    private static bool HasMigrations(string context)
    {
        string migrations = Path.Combine(context, "Migrations");
        return Directory.Exists(migrations) && Directory.EnumerateFiles(migrations, "*ModelSnapshot.cs").Any();
    }

    private static bool TemplateMaintenance(LocatedProject project) =>
        File.Exists(Path.Combine(project.Root, ".template.config", "template.json"));

    private static string? LauncherSolution(LocatedProject project) =>
        File.Exists(Path.Combine(project.Root, "Launcher", "CleanArch.Create.slnx"))
            ? Path.Combine("Launcher", "CleanArch.Create.slnx")
            : null;

    private static ExternalCall Dotnet(IReadOnlyList<string> arguments, string workingDirectory) =>
        new("dotnet", arguments, workingDirectory);

    private static ExternalCall Pnpm(IReadOnlyList<string> arguments, string workingDirectory, string? note = null) =>
        new(_pnpm, arguments, workingDirectory, DirectoryNote: note);

    private static string Tool(string fileName) => Path.Combine("Tools", fileName);

    private static string WebFolder(LocatedProject project) => Path.Combine(project.Root, "Src", "Web");

    private sealed class TeeWriter(TextWriter first, TextWriter second) : TextWriter
    {
        public override Encoding Encoding => first.Encoding;

        public override void Write(char value)
        {
            first.Write(value);
            second.Write(value);
        }

        public override void Write(string? value)
        {
            first.Write(value);
            second.Write(value);
        }

        public override void Write(char[] buffer, int index, int count)
        {
            first.Write(buffer, index, count);
            second.Write(buffer, index, count);
        }

        public override void Flush()
        {
            first.Flush();
            second.Flush();
        }
    }
}

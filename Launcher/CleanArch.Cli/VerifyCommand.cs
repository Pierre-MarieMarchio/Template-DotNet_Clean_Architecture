using System.CommandLine;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CleanArch.Cli;

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

    private static readonly string[] _styleRulesTheBuildLacks = ["IDE0003", "IDE0049"];

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
        if (context.Project is { } project)
        {
            verify.SetAction(parseResult => Run(project, parseResult));
        }

        return verify;
    }

    private static int Run(LocatedProject project, ParseResult parseResult)
    {
        TextWriter output = parseResult.InvocationConfiguration.Output;
        TextWriter error = parseResult.InvocationConfiguration.Error;

        if (MissingInitialMigration(project) is { } missing)
        {
            error.WriteLine(
                $"'verify' needs the initial migration of {missing}, and there is none yet. "
                + "Run `dotnet run Tools/Tasks.cs bootstrap`, which creates it with migration-initial, then commit it (docs/guides/migrations.md).");
            return CleanArchCommand.Failed;
        }

        foreach (Func<int> step in Steps(project, output, error))
        {
            int exit = step();
            if (exit != CleanArchCommand.Succeeded)
            {
                return exit;
            }
        }

        return CleanArchCommand.Succeeded;
    }

    private static IEnumerable<Func<int>> Steps(LocatedProject project, TextWriter output, TextWriter error)
    {
        foreach (ExternalCall call in GateCalls(project))
        {
            yield return () => ExternalCommand.Run(call, output, error);
        }

        if (project.File.Web is not null)
        {
            yield return () => ExternalCommand.Run(Pnpm(["--version"], project.Root), output, error);
        }

        if (project.File.Client)
        {
            yield return () => ApiClientCheck(project, output, error);
        }

        if (project.File.Web is not null)
        {
            yield return () => WebSchema(project, output, error);
            yield return () => ExternalCommand.Run(Pnpm(["audit"], WebFolder(project), _webNote), output, error);
            yield return () => ExternalCommand.Run(Pnpm(["install", "--frozen-lockfile"], WebFolder(project), _webNote), output, error);
            foreach (string script in _webScripts)
            {
                yield return () => ExternalCommand.Run(Pnpm([script], WebFolder(project), _webNote), output, error);
            }
        }

        if (TemplateMaintenance(project))
        {
            yield return () => ExternalCommand.Run(Dotnet(["run", Tool("Tasks.cs"), "web-lockfiles", "--check"], project.Root), output, error);
        }

        string solution = $"{project.File.Name}.sln";
        yield return () => ExternalCommand.Run(Dotnet(["restore", solution], project.Root), output, error);
        yield return () => ExternalCommand.Run(Dotnet(["format", solution, "--diagnostics", .. _styleRulesTheBuildLacks, "--verify-no-changes", "--no-restore"], project.Root), output, error);
        yield return () => ExternalCommand.Run(Dotnet(["build", solution, "--configuration", "Debug", "--no-restore"], project.Root), output, error);
        yield return () => ExternalCommand.Run(Dotnet(["test", "--solution", solution, "--configuration", "Debug", "--no-build"], project.Root), output, error);

        foreach (string context in Contexts(project))
        {
            yield return () => ExternalCommand.Run(
                Dotnet(["ef", "migrations", "has-pending-model-changes", "--project", context, "--startup-project", context, "--no-build"], project.Root),
                output,
                error);
        }

        yield return () => VulnerablePackages(project, solution, output, error);

        if (LauncherSolution(project) is { } launcher)
        {
            yield return () => ExternalCommand.Run(Dotnet(["restore", launcher], project.Root), output, error);
            yield return () => ExternalCommand.Run(Dotnet(["format", launcher, "--diagnostics", .. _styleRulesTheBuildLacks, "--verify-no-changes", "--no-restore"], project.Root), output, error);
            yield return () => ExternalCommand.Run(Dotnet(["build", launcher, "--configuration", "Debug", "--no-restore"], project.Root), output, error);
            yield return () => ExternalCommand.Run(Dotnet(["test", "--solution", launcher, "--configuration", "Debug", "--no-build"], project.Root), output, error);
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

    private static int WebSchema(LocatedProject project, TextWriter output, TextWriter error)
    {
        string root = project.Root;
        if (!File.Exists(Path.Combine(root, "Src", "Web", "packages", "api-client", "scripts", "generate.mjs")))
        {
            output.WriteLine("Src/Web/packages/api-client/scripts/generate.mjs is missing, so the web schema is not checked.");
            return CleanArchCommand.Succeeded;
        }

        string reference = OpenApiReference(project);
        if (!File.Exists(Path.Combine(root, reference)))
        {
            error.WriteLine($"{reference} is missing: run `dotnet run Tools/Tasks.cs openapi` first.");
            return CleanArchCommand.Failed;
        }

        int installed = ExternalCommand.Run(Pnpm(["install", "--frozen-lockfile"], WebFolder(project), _webNote), output, error);
        if (installed != CleanArchCommand.Succeeded)
        {
            return installed;
        }

        string package = Path.Combine(root, "Src", "Web", "packages", "api-client");
        return ExternalCommand.Run(
            Pnpm(["exec", "node", "scripts/generate.mjs", "--check", reference], package, _apiClientPackageNote),
            output,
            error);
    }

    private static int ApiClientCheck(LocatedProject project, TextWriter output, TextWriter error)
    {
        string root = project.Root;
        string reference = OpenApiReference(project);
        string client = ClientFolder(project);
        string generated = Path.Combine(client, "Generated");

        if (!Directory.Exists(Path.Combine(root, client)))
        {
            error.WriteLine($"{client} does not exist, so there is no client to check: a presentation that calls the API through it brings it.");
            return CleanArchCommand.Failed;
        }

        if (!File.Exists(Path.Combine(root, reference)))
        {
            error.WriteLine($"{reference} is missing: run `dotnet run Tools/Tasks.cs openapi` first.");
            return CleanArchCommand.Failed;
        }

        string written = Path.Combine(Path.GetTempPath(), "api-client-" + Guid.NewGuid().ToString("N"));
        try
        {
            int exit = ExternalCommand.Run(
                Dotnet(
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
                    root),
                output,
                error);
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
                    + "Run `dotnet run Tools/Tasks.cs api-client` and commit what it writes.");
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

    private static int VulnerablePackages(LocatedProject project, string solution, TextWriter output, TextWriter error)
    {
        var listed = new StringWriter();
        int exit = ExternalCommand.Run(
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
        string[] missing = [.. Contexts(project)
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

    private static IReadOnlyList<string> Contexts(LocatedProject project)
    {
        string persistence = ContextFolder(project, "Persistence");
        return project.File.Options.Auth == "local"
            ? [persistence, ContextFolder(project, "Auth")]
            : [persistence];
    }

    private static string ContextFolder(LocatedProject project, string part) =>
        Path.Combine("Src", "Infrastructure", $"{Initials(project.File.Name)}.{part}");

    private static string OpenApiReference(LocatedProject project) =>
        Path.Combine("Tests", "Integration", $"{Initials(project.File.Name)}.Api.IntegrationTests", "OpenApi", "v1.json");

    private static string ClientFolder(LocatedProject project) =>
        Path.Combine("Src", "Clients", $"{Initials(project.File.Name)}.Client");

    private static string Initials(string name) =>
        string.Concat(name.Split('.').Select(SegmentInitials));

    private static string SegmentInitials(string segment) =>
        segment.Any(char.IsAsciiLetterUpper)
            ? string.Concat(segment.Where(char.IsAsciiLetterUpper))
            : string.Concat(segment.Where(char.IsAsciiLetter).Take(1).Select(char.ToUpperInvariant));

    private static bool TemplateMaintenance(LocatedProject project) =>
        File.Exists(Path.Combine(project.Root, ".template.config", "template.json"));

    private static string? LauncherSolution(LocatedProject project) =>
        File.Exists(Path.Combine(project.Root, "Launcher", "CleanArch.Create.slnx"))
            ? Path.Combine("Launcher", "CleanArch.Create.slnx")
            : null;

    private static ExternalCall Dotnet(IReadOnlyList<string> arguments, string workingDirectory) =>
        new("dotnet", arguments, workingDirectory);

    private static ExternalCall Pnpm(IReadOnlyList<string> arguments, string workingDirectory, string? note = null) =>
        new(_pnpm, arguments, workingDirectory, note);

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

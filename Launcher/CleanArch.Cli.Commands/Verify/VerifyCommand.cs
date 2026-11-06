using System.CommandLine;
using System.Text;
using CleanArch.Cli.Commands.Hosting;
using CleanArch.Cli.Commands.Kernel;

namespace CleanArch.Cli.Commands.Verify;

internal static class VerifyCommand
{
    public const string Name = "verify";

    public static readonly CommandRegistration Registration = new(ProjectCommand.Refusal, Create);

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

    private static readonly string[][] _webChecks = [["audit"], ["install", "--frozen-lockfile"], ["lint"], ["test"], ["build"]];

    private const string _vulnerableMarker = "has the following vulnerable packages";

    private static Command Create(CommandContext context)
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

    private static int Run(LocatedProject project, Shell shell, BuildSettings settings) =>
        PersistenceContexts.WithoutInitialMigration(project, Name, ProjectLayout.Contexts(project)) is { } missing
            ? shell.Fail(missing)
            : ExitCode.FirstFailureOf(Steps(project, shell, settings));

    private static IEnumerable<Func<int>> Steps(LocatedProject project, Shell shell, BuildSettings settings)
    {
        string root = project.Root;
        string solution = ProjectLayout.Solution(project);
        bool web = project.File.Web is not null;
        return
        [
            .. Running(shell, GateCalls(project)),
            .. Running(shell, web ? [Pnpm.Call(root, "--version")] : []),
            .. project.File.Client ? [() => ApiClientCheck(project, shell)] : Array.Empty<Func<int>>(),
            .. web ? [() => WebSchemaCheck(project, shell)] : Array.Empty<Func<int>>(),
            .. Running(shell, web ? _webChecks.Select(arguments => Pnpm.In(project, ProjectLayout.Web, arguments)) : []),
            .. Running(shell, TemplateMaintenance(project) ? [Dotnet.RunFile(root, ProjectLayout.Tasks, "web-lockfiles", "--check")] : []),
            .. Running(
                shell,
                [
                    Dotnet.Call(root, ["restore", solution, .. settings.Nodes]),
                    Dotnet.StyleCheck(root, solution, "--no-restore"),
                    Dotnet.Build(root, solution, settings.Configuration, ["--no-restore", .. settings.Nodes]),
                ]),
            () => TestRuns.RunBuilt(project, shell, settings),
            .. Running(shell, ProjectLayout.Contexts(project).Select(context => Dotnet.Ef(root, context, ["migrations", "has-pending-model-changes"], "--no-build"))),
            () => VulnerablePackages(project, shell, solution),
            .. Running(shell, LauncherCalls(project, settings)),
        ];
    }

    private static IEnumerable<Func<int>> Running(Shell shell, IEnumerable<ExternalCall> calls) =>
        calls.Select(call => (Func<int>)(() => shell.Run(call)));

    private static IEnumerable<ExternalCall> GateCalls(LocatedProject project) =>
    [
        .. _selfTestedGates.Select(gate => SelfTest(project, gate)),
        .. OptionalGate(project, "CheckDocPaths.cs"),
        .. OptionalGate(project, "CheckWorkflows.cs"),
        SelfTest(project, "CoverageGate.cs"),
        .. OptionalGate(project, "CheckComments.cs"),
        Dotnet.RunFile(project.Root, ProjectLayout.Tasks, "docs-index", "--check"),
    ];

    private static ExternalCall SelfTest(LocatedProject project, string gate) =>
        Dotnet.RunFile(project.Root, ProjectLayout.Tool(gate), "--self-test");

    private static IEnumerable<ExternalCall> OptionalGate(LocatedProject project, string gate) =>
        File.Exists(Path.Combine(project.Root, ProjectLayout.Tool(gate)))
            ? [SelfTest(project, gate), Dotnet.RunFile(project.Root, ProjectLayout.Tool(gate), project.Root)]
            : [];

    private static IEnumerable<ExternalCall> LauncherCalls(LocatedProject project, BuildSettings settings)
    {
        string launcher = Path.Combine("Launcher", "CleanArch.Create.slnx");
        string root = project.Root;
        return File.Exists(Path.Combine(root, launcher))
            ?
            [
                Dotnet.Call(root, "restore", launcher),
                Dotnet.StyleCheck(root, launcher, "--no-restore"),
                Dotnet.Build(root, launcher, settings.Configuration, "--no-restore"),
                Dotnet.Test(root, launcher, settings.Configuration, "--no-build"),
            ]
            : [];
    }

    private static bool TemplateMaintenance(LocatedProject project) =>
        File.Exists(Path.Combine(project.Root, ".template.config", "template.json"));

    private static int WebSchemaCheck(LocatedProject project, Shell shell)
    {
        if (!WebSchema.IsGenerated(project))
        {
            shell.Output.WriteLine($"{ProjectPath.Shown(ProjectLayout.WebSchemaScript)} is missing, so the web schema is not checked.");
            return ExitCode.Succeeded;
        }

        if (!File.Exists(OpenApiReference.PathOf(project)))
        {
            return shell.Fail(OpenApiReference.Missing(project));
        }

        return ExitCode.FirstFailureOf(
        [
            () => shell.Run(Pnpm.In(project, ProjectLayout.Web, "install", "--frozen-lockfile")),
            () => shell.Run(WebSchema.Check(project, ProjectLayout.OpenApiReference(project))),
        ]);
    }

    private static int ApiClientCheck(LocatedProject project, Shell shell) =>
        ApiClient.Check(project, shell, ProjectLayout.OpenApiReference(project), "api-client-", "check");

    private static int VulnerablePackages(LocatedProject project, Shell shell, string solution)
    {
        using var listed = new StringWriter();
        int exit = shell.Run(
            Dotnet.Call(project.Root, "list", solution, "package", "--vulnerable", "--include-transitive"),
            new TeeWriter(shell.Output, listed),
            shell.Error);
        if (exit != ExitCode.Succeeded)
        {
            return exit;
        }

        return listed.ToString().Contains(_vulnerableMarker, StringComparison.OrdinalIgnoreCase)
            ? shell.Fail(
                "A package with a known vulnerability is referenced, directly or transitively. The listing above names it. "
                + "Pin a patched version in Directory.Packages.props, under a Security pins label, with a comment saying when the pin can be removed.")
            : ExitCode.Succeeded;
    }

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

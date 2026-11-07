using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CleanArch.Tooling.Processes;

namespace CleanArch.Create.Setup;

public sealed partial class BootstrapStep()
    : SetupStep("Bootstrap", DescriptionFor(_ => false), $"Running {Restore}, then {Command}; this takes a few minutes", Commands)
{
    public const string Task = "bootstrap";
    public const string Restore = "dotnet tool restore";
    public const string Command = "dotnet cleanarch " + Task;

    public static readonly IReadOnlyList<string> Commands = [Restore, Command];

    public static readonly IReadOnlyList<(string Symbol, string Writes)> WritesWhen =
    [
        ("scaffoldedAtBootstrap", "creates each project or app a scaffold.json describes"),
        ("dotnetClient", "writes the .NET client"),
        ("anyWeb", "installs the web workspace, which needs Node.js and pnpm"),
    ];

    private const string _toolPackage = "CleanArch.Cli";

    public static string DescriptionFor(Func<string, bool> holds) =>
        $"Runs {Restore}, then {Command}: "
        + string.Join(
            ", ",
            [
                "formatting, OpenAPI document, tools and first migrations",
                .. WritesWhen.Where(fact => holds(fact.Symbol)).Select(fact => fact.Writes),
            ])
        + ". Needs internet.";

    internal static bool ProjectToolNotFound(string output) =>
        output.Contains(_toolPackage, StringComparison.OrdinalIgnoreCase) && MissingPackage().IsMatch(output);

    internal override SetupOutcome Run(SetupContext context, IReadOnlyList<SetupOutcome> before)
    {
        ProcessResult restore = Dotnet(context, "tool", "restore");
        if (restore.ExitCode != 0)
        {
            string? journal = ProcessFailure.Journal(context.Root, Task, Restore, restore);
            return ProjectToolNotFound(restore.Output)
                ? new SetupOutcome(this, SetupStatus.Failed, ProcessFailure.Why(Restore, restore, journal, ProjectToolCause(context.Version)), [RestoreFromAFolder(context.Version), Command])
                : Failed(Restore, restore, Commands, journal);
        }

        ProcessResult bootstrap = Dotnet(context, "cleanarch", Task);
        return bootstrap.ExitCode == 0
            ? new SetupOutcome(this, SetupStatus.Done, null, [])
            : Failed(Command, bootstrap, [Command], ProcessFailure.Journal(context.Root, Task, Command, bootstrap));
    }

    internal override List<string> Rerun(SetupContext context) => [.. Commands];

    private static ProcessResult Dotnet(SetupContext context, params string[] arguments) =>
        context.Processes.Run(new ProcessCall("dotnet", context.Root, arguments, ReadOnlyDictionary<string, string>.Empty), context.Echo);

    private static string ProjectToolCause(string version) =>
        $"{_toolPackage} {version}, the tool .config/dotnet-tools.json pins, isn't on a NuGet source this machine uses: nuget.org holds only published releases, "
        + "so a version not released yet, or a template packed locally, has no package there. "
        + $"Put {_toolPackage}.{version}.nupkg (dotnet pack Launcher/{_toolPackage} in the template's repository) in a folder, restore the tools from there, then run {Command}.";

    private static string RestoreFromAFolder(string version) =>
        $"{Restore} --add-source <folder holding {_toolPackage}.{version}.nupkg>";

    [GeneratedRegex(@"NU110[123]|Unable to find package|not found in NuGet feeds", RegexOptions.IgnoreCase)]
    private static partial Regex MissingPackage();
}

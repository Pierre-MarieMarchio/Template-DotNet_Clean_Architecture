using System.Text.RegularExpressions;
using CleanArch.Tooling.Processes;

namespace CleanArch.Create.Setup;

public sealed partial class BootstrapStep()
    : SetupStep("Bootstrap", DescriptionFor(_ => false), "Running bootstrap, this takes a few minutes", Command)
{
    public const string Task = "bootstrap";
    public const string Command = "dotnet run Tools/Tasks.cs " + Task;

    public static readonly IReadOnlyList<(string Symbol, string Writes)> WritesWhen =
    [
        ("scaffoldedAtBootstrap", "creates each project or app a scaffold.json describes"),
        ("dotnetClient", "writes the .NET client"),
        ("anyWeb", "installs the web workspace, which needs Node.js and pnpm"),
    ];

    private const string _toolPackage = "CleanArch.Cli";

    private static readonly IReadOnlyDictionary<string, string> _noVariables = new Dictionary<string, string>(StringComparer.Ordinal);

    public static string DescriptionFor(Func<string, bool> holds) =>
        "Runs " + Command + ": "
        + string.Join(
            ", ",
            [
                "formatting, OpenAPI document, tools and first migrations",
                .. WritesWhen.Where(fact => holds(fact.Symbol)).Select(fact => fact.Writes),
            ])
        + ". Needs internet.";

    internal static bool ProjectToolNotFound(string output) =>
        output.Contains(_toolPackage, StringComparison.Ordinal) && MissingPackage().IsMatch(output);

    internal override SetupOutcome Run(SetupContext context, IReadOnlyList<SetupOutcome> before)
    {
        ProcessResult result = context.Processes.Run(new ProcessCall("dotnet", context.Root, ["run", "Tools/Tasks.cs", "bootstrap"], _noVariables), context.Echo);
        if (result.ExitCode == 0)
        {
            return new SetupOutcome(this, SetupStatus.Done, null, []);
        }

        string? journal = ProcessFailure.Journal(context.Root, Task, Command, result);
        return ProjectToolNotFound(result.Output)
            ? new SetupOutcome(this, SetupStatus.Failed, ProcessFailure.Why(Command, result, journal, ProjectToolCause(context.Version)), [ProjectToolInstall(context.Version), Command])
            : Failed(Command, result, [Command], journal);
    }

    internal override List<string> Rerun(SetupContext context) => [Command];

    private static string ProjectToolCause(string version) =>
        $"{_toolPackage} {version}, the tool this project pins, isn't on a NuGet source this machine uses: nuget.org holds only published releases, "
        + "so a version not released yet, or a template packed locally, has no package there. "
        + $"Put {_toolPackage}.{version}.nupkg (dotnet pack Launcher/{_toolPackage} in the template's repository) in a folder, install it from there, then run bootstrap again.";

    private static string ProjectToolInstall(string version) =>
        $"dotnet tool install {_toolPackage} --local --version {version} --add-source <folder holding {_toolPackage}.{version}.nupkg>";

    [GeneratedRegex(@"NU110[123]|Unable to find package|not found in NuGet feeds", RegexOptions.IgnoreCase)]
    private static partial Regex MissingPackage();
}

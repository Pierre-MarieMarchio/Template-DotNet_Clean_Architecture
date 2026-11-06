using CleanArch.Tooling.Output;
using CleanArch.Tooling.Templates;

namespace CleanArch.Cli.UpgradeReport;

internal sealed class ReportProcesses(IProcessLauncher launcher, TextWriter report)
{
    private static readonly IReadOnlyDictionary<string, string> _environment = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
        ["DOTNET_NOLOGO"] = "1",
    };

    public TemplateEngine EngineIn(string hive) =>
        new(Terminal.Over(report), hive, echo: true, new ExternalProcessRunner(launcher));

    public (int ExitCode, string Output, string Errors) Capture(string directory, string executable, params IReadOnlyList<string> arguments)
    {
        using var output = new StringWriter();
        using var errors = new StringWriter();
        int exitCode = launcher.Launch(Call(directory, executable, arguments), output, errors);

        return (exitCode, output.ToString(), errors.ToString());
    }

    private static ExternalCall Call(string directory, string executable, IReadOnlyList<string> arguments) =>
        new(executable, arguments, directory, _environment, CloseInput: true);
}

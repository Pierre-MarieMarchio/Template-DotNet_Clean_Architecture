using CleanArch.Tooling;
using CleanArch.Tooling.Manifests;
using Spectre.Console;

namespace CleanArch.Create;

public sealed class TemplateEngine
{
    private static readonly IReadOnlyDictionary<string, string> _environment = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["DOTNET_CLI_UI_LANGUAGE"] = "en",
    };

    public const string PostActionsHeading = "Processing post-creation actions...";

    private readonly IAnsiConsole _console;
    private readonly string? _hive;
    private readonly bool _echo;
    private readonly IProcessRunner _processes;

    public TemplateEngine(IAnsiConsole console, string? hive, bool echo, IProcessRunner processes)
    {
        _console = console;
        _hive = hive;
        _echo = echo;
        _processes = processes;
    }

    public static string CommandLine(string prefix, IEnumerable<string> arguments) =>
        string.Join(' ', arguments.Select(Quoted).Prepend(prefix));

    public static string? InstalledVersion(string listing, string packageId)
    {
        string[] lines = listing.Split('\n', StringSplitOptions.TrimEntries);
        int package = Array.FindIndex(lines, line => string.Equals(line, packageId, StringComparison.OrdinalIgnoreCase));
        const string prefix = "Version:";

        return package >= 0 && package + 1 < lines.Length && lines[package + 1].StartsWith(prefix, StringComparison.Ordinal)
            ? lines[package + 1][prefix.Length..].Trim()
            : null;
    }

    public void EnsureInstalled(TemplateSource source, string version, string workingDirectory)
    {
        (int exitCode, string listing) = Execute(workingDirectory, ["uninstall"], echo: false);
        string? installed = exitCode == 0 ? InstalledVersion(listing, TemplateSource.PackageId) : null;

        if (source.LocalPath is { } path && Directory.Exists(path))
        {
            Run(workingDirectory, ["install", path, "--force"]);
        }
        else if (source.LocalPath is { } package)
        {
            if (installed is not null)
            {
                Run(workingDirectory, ["uninstall", TemplateSource.PackageId]);
            }

            Run(workingDirectory, ["install", package]);
        }
        else if (installed != version)
        {
            Run(workingDirectory, ["install", $"{TemplateSource.PackageId}@{version}"]);
        }
        else if (_echo)
        {
            _console.MarkupLine($"[grey]{TemplateSource.PackageId} {Markup.Escape(version)} is installed.[/]");
        }
    }

    public void Run(string workingDirectory, IReadOnlyList<string> arguments, IReadOnlyCollection<string>? performed = null)
    {
        (int exitCode, string output) = Execute(workingDirectory, arguments, echo: true, performed ?? []);
        if (exitCode != 0)
        {
            string failure = $"{CommandLine("dotnet new", arguments)} failed with exit code {exitCode}.";
            throw new LauncherException(_echo ? failure : output.TrimEnd() + Environment.NewLine + failure);
        }
    }

    private (int ExitCode, string Output) Execute(string workingDirectory, IReadOnlyList<string> arguments, bool echo, IReadOnlyCollection<string>? performed = null)
    {
        List<string> command = ["new", .. arguments];
        if (_hive is not null)
        {
            command.AddRange(["--debug:custom-hive", _hive]);
        }

        Action<string>? relay = echo && _echo ? _console.Verbatim : null;
        InstructionFilter? filter = relay is not null && performed is { Count: > 0 } ? new InstructionFilter(relay, performed) : null;
        ProcessResult result = _processes.Run(
            new ProcessCall("dotnet", workingDirectory, command, _environment),
            filter is null ? relay : filter.Line);
        filter?.Flush();

        return (result.ExitCode, result.Output);
    }

    private sealed class InstructionFilter(Action<string> relay, IReadOnlyCollection<string> performed)
    {
        private const string _descriptionPrefix = "Description:";

        private readonly List<string> _instruction = [];
        private bool _reached;
        private bool _headingRelayed;

        public void Line(string line)
        {
            if (!_reached)
            {
                _reached = line.Trim() == PostActionsHeading;
                if (!_reached)
                {
                    relay(line);
                }

                return;
            }

            if (line.StartsWith(_descriptionPrefix, StringComparison.Ordinal))
            {
                Flush();
            }

            _instruction.Add(line);
        }

        public void Flush()
        {
            string text = string.Join('\n', _instruction);
            if (_instruction.Count > 0 && !performed.Any(task => text.Contains(task, StringComparison.OrdinalIgnoreCase)))
            {
                if (!_headingRelayed)
                {
                    relay(PostActionsHeading);
                    _headingRelayed = true;
                }

                _instruction.ForEach(relay);
            }

            _instruction.Clear();
        }
    }

    private static string Quoted(string argument) =>
        argument.Length > 0 && argument.All(character => char.IsLetterOrDigit(character) || "-_./@:=+".Contains(character, StringComparison.Ordinal))
            ? argument
            : "\"" + argument.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}

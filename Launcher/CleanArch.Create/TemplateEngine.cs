using System.Diagnostics;
using System.Text;
using CleanArch.Create.Manifests;
using Spectre.Console;

namespace CleanArch.Create;

public sealed class TemplateEngine
{
    private readonly IAnsiConsole _console;
    private readonly string? _hive;
    private readonly Lock _output = new();

    public TemplateEngine(IAnsiConsole console, string? hive)
    {
        _console = console;
        _hive = hive;
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
        else if (installed == version)
        {
            _console.MarkupLine($"[grey]{TemplateSource.PackageId} {Markup.Escape(version)} is installed.[/]");
        }
        else
        {
            Run(workingDirectory, ["install", $"{TemplateSource.PackageId}@{version}"]);
        }
    }

    public void Run(string workingDirectory, IReadOnlyList<string> arguments)
    {
        (int exitCode, _) = Execute(workingDirectory, arguments, echo: true);
        if (exitCode != 0)
        {
            throw new LauncherException($"{CommandLine("dotnet new", arguments)} failed with exit code {exitCode}.");
        }
    }

    private (int ExitCode, string Output) Execute(string workingDirectory, IReadOnlyList<string> arguments, bool echo)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        start.ArgumentList.Add("new");
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        if (_hive is not null)
        {
            start.ArgumentList.Add("--debug:custom-hive");
            start.ArgumentList.Add(_hive);
        }

        var captured = new StringBuilder();
        using var process = new Process { StartInfo = start };
        DataReceivedEventHandler collect = (_, line) =>
        {
            if (line.Data is null)
            {
                return;
            }

            lock (_output)
            {
                captured.AppendLine(line.Data);
                if (echo)
                {
                    _console.Verbatim(line.Data);
                }
            }
        };
        process.OutputDataReceived += collect;
        process.ErrorDataReceived += collect;

        if (!process.Start())
        {
            throw new LauncherException("dotnet could not be started; the .NET SDK must be on the PATH.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        return (process.ExitCode, captured.ToString());
    }

    private static string Quoted(string argument) =>
        argument.Length > 0 && argument.All(character => char.IsLetterOrDigit(character) || "-_./@:=+".Contains(character, StringComparison.Ordinal))
            ? argument
            : "\"" + argument.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}

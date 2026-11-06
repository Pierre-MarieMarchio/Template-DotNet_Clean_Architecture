using System.Text.RegularExpressions;

namespace CleanArch.Tooling;

public static partial class ProcessFailure
{
    private const int _tail = 6;
    private const string _journalFolder = "artifacts";

    public static string Why(string command, ProcessResult result, string? journal = null, string? knownCause = null)
    {
        string[] lines = [.. result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        string[] tail = [.. lines.TakeLast(_tail)];
        string? lastError = lines.LastOrDefault(line => ErrorLine().IsMatch(line));
        var why = new List<string> { $"{command} exited with code {result.ExitCode}." };
        if (knownCause is not null)
        {
            why.Add("Last error: " + (lastError ?? lines.LastOrDefault() ?? "none printed."));
            why.Add(knownCause);
        }
        else
        {
            if (lastError is not null && !tail.Contains(lastError))
            {
                why.Add("Last error: " + lastError);
            }

            why.AddRange(tail);
        }

        if (journal is not null)
        {
            why.Add("The full output is in " + journal);
        }

        return string.Join(Environment.NewLine, why);
    }

    public static string? Journal(string root, string name, string command, ProcessResult result)
    {
        try
        {
            string folder = Path.Combine(root, _journalFolder);
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, name + ".log");
            File.WriteAllText(path, $"> {command}{Environment.NewLine}exit code {result.ExitCode}{Environment.NewLine}{Environment.NewLine}{result.Output}");
            return path;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"\berror\s+[A-Z]{2,}\d+|^error:|\bNU\d{4}\b|\bfatal:|not found in NuGet feeds|unhandled exception", RegexOptions.IgnoreCase)]
    private static partial Regex ErrorLine();
}

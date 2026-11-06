namespace CleanArch.Cli.Generators.Shared;

internal sealed class ProjectDocuments(string root, string needer, List<string> problems)
{
    internal const string ConditionOpen = "#" + "if (";

    internal const string ConditionClose = "#" + "endif";

    private readonly Dictionary<string, string> _loaded = new(StringComparer.Ordinal);

    internal string Load(string relative)
    {
        if (_loaded.TryGetValue(relative, out string? loaded))
        {
            return loaded;
        }

        string full = Path.Combine(root, relative);

        if (!File.Exists(full))
        {
            problems.Add($"{relative} is not there, and {needer} needs it. Nothing is written until it is.");

            return string.Empty;
        }

        string text = SourceText.Decode(File.ReadAllBytes(full));

        if (text.Contains('\r', StringComparison.Ordinal))
        {
            problems.Add($"{relative} has CRLF line endings; this project keeps LF. Run format-fix first.");
        }

        if (text.Contains(ConditionOpen, StringComparison.Ordinal))
        {
            problems.Add($"{relative} still holds template conditions: this is the template's own tree, not a generated project. Generate a project, then run this there.");
        }

        _loaded[relative] = text;

        return text;
    }
}

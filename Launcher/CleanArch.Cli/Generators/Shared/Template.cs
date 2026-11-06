using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace CleanArch.Cli.Generators.Shared;

internal static class Template
{
    private static readonly HashSet<string> _knownFlags = new(StringComparer.Ordinal)
    {
        "get", "list", "create", "update", "delete", "owner", "shared", "rec", "agg", "etag",
        "content", "change", "wq", "dto", "single", "rq", "notfound", "clock", "stage",
        "purge", "anon", "fields", "anontext", "trim", "policy", "prefixed",
    };

    private static readonly Regex _tokenPattern = new("%([A-Za-z]+)%", RegexOptions.None, TimeSpan.FromSeconds(2));

    private static readonly Regex _directivePattern = new(
        @"^\s*%(?:if (?<condition>[^%]+)|(?<else>else)|(?<end>end))%\s*$",
        RegexOptions.None,
        TimeSpan.FromSeconds(2));

    private static readonly Regex _markedUsing = new(
        @"^using (?<namespace>[^;]+);\s*//\?(?<types>.+)$",
        RegexOptions.None,
        TimeSpan.FromSeconds(2));

    internal static bool Evaluate(string condition, HashSet<string> flags) =>
        condition.Split('|').Any(group => group.Split('&').All(atom =>
        {
            string trimmed = atom.Trim();
            bool negated = trimmed.StartsWith('!');
            string name = negated ? trimmed[1..] : trimmed;

            if (!_knownFlags.Contains(name))
            {
                throw new InvalidOperationException($"'{name}' is not a flag a template may name.");
            }

            return flags.Contains(name) != negated;
        }));

    internal static string Substitute(string text, Dictionary<string, string> tokens)
    {
        for (int pass = 0; pass < 3; pass++)
        {
            string next = _tokenPattern.Replace(
                text,
                match => tokens.TryGetValue(match.Groups[1].Value, out string? value) ? value : match.Value);

            if (string.Equals(next, text, StringComparison.Ordinal))
            {
                break;
            }

            text = next;
        }

        return text;
    }

    internal static string Render(string template, HashSet<string> flags, Dictionary<string, string> tokens)
    {
        var kept = new List<string>();
        var frames = new Stack<(bool Parent, bool Condition)>();
        bool emitting = true;

        foreach (string line in template.ReplaceLineEndings("\n").Split('\n'))
        {
            var directive = _directivePattern.Match(line);

            if (!directive.Success)
            {
                if (emitting)
                {
                    kept.Add(line);
                }

                continue;
            }

            if (directive.Groups["condition"].Success)
            {
                bool holds = Evaluate(directive.Groups["condition"].Value, flags);

                frames.Push((emitting, holds));
                emitting = emitting && holds;
            }
            else if (directive.Groups["else"].Success)
            {
                var (parent, condition) = frames.Peek();

                emitting = parent && !condition;
            }
            else
            {
                emitting = frames.Pop().Parent;
            }
        }

        if (frames.Count > 0)
        {
            throw new InvalidOperationException("A template opens an %if% it never closes.");
        }

        return Tidy(PruneUsings(Substitute(string.Join('\n', kept), tokens)));
    }

    private static string PruneUsings(string text)
    {
        string[] lines = text.Split('\n');
        int declaration = Array.FindIndex(lines, line => line.StartsWith("namespace ", StringComparison.Ordinal));

        if (declaration < 0)
        {
            throw new InvalidOperationException("A template declares no namespace.");
        }

        string body = string.Join('\n', lines[declaration..]);
        var usings = new List<string>();

        foreach (string line in lines[..declaration].Where(line => line.StartsWith("using ", StringComparison.Ordinal)))
        {
            var marked = _markedUsing.Match(line);

            if (!marked.Success)
            {
                usings.Add(line);

                continue;
            }

            if (Regex.IsMatch(body, $@"\b(?:{marked.Groups["types"].Value.Trim()})\b", RegexOptions.None, TimeSpan.FromSeconds(2)))
            {
                usings.Add($"using {marked.Groups["namespace"].Value};");
            }
        }

        string[] sorted =
        [
            .. usings.Distinct(StringComparer.Ordinal)
                .OrderBy(line => !line.StartsWith("using System", StringComparison.Ordinal))
                .ThenBy(line => line.TrimEnd(';'), StringComparer.Ordinal),
        ];

        return sorted.Length == 0 ? body : string.Join('\n', sorted) + "\n\n" + body;
    }

    private static string Tidy(string text)
    {
        var lines = new List<string>();

        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd();

            if (line.Length == 0 && (lines.Count == 0 || lines[^1].Length == 0 || lines[^1].EndsWith('{')))
            {
                continue;
            }

            if (line.TrimStart().StartsWith('}') && lines.Count > 0 && lines[^1].Length == 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            lines.Add(line);
        }

        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return string.Join('\n', lines) + "\n";
    }
}

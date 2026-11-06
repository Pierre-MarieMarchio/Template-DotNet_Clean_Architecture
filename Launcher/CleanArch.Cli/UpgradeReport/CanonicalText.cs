using System.Text;
using System.Text.RegularExpressions;

namespace CleanArch.Cli.UpgradeReport;

internal static class CanonicalText
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(2);

    private static readonly Regex _usingDirective = new(@"^(global )?using [A-Za-z_][^(;]*;$", RegexOptions.None, _timeout);

    private static readonly Regex _solutionEntry = new(
        @"^Project\(""\{[^}]+\}""\)\s*=\s*""[^""]*"",\s*""(?<path>[^""]*)"",\s*""\{(?<id>[0-9A-Fa-f-]{36})\}""",
        RegexOptions.Multiline,
        _timeout);

    private static readonly Regex _guid = new(
        @"(?<![0-9A-Fa-f-])[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}(?![0-9A-Fa-f-])",
        RegexOptions.None,
        _timeout);

    public static IEnumerable<string> Guids(string text) => _guid.Matches(text).Select(match => match.Value);

    public static bool IsBinary(byte[] bytes) => bytes.AsSpan(0, Math.Min(bytes.Length, 8000)).Contains((byte)0);

    public static string Normalised(byte[] bytes)
    {
        string text = Encoding.UTF8.GetString(bytes);

        return (text.StartsWith('﻿') ? text[1..] : text).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    public static string WithoutDrawnGuids(string text, IReadOnlySet<string> heldGuids)
    {
        var drawn = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        return _guid.Replace(text, match => heldGuids.Contains(match.Value) ? match.Value : $"<GUID drawn #{OrderOf(drawn, match.Value)}>");
    }

    public static string Solution(string text)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (GroupCollection entry in _solutionEntry.Matches(text).Select(match => match.Groups))
        {
            string path = entry["path"].Value;
            seen[path] = seen.GetValueOrDefault(path) + 1;
            text = Regex.Replace(text, Regex.Escape(entry["id"].Value), $"<{path}#{seen[path]}>", RegexOptions.IgnoreCase, _timeout);
        }

        return text;
    }

    public static string CSharp(string text)
    {
        var usings = new List<string>();
        var rest = new List<string>();
        bool header = true;

        foreach (string line in text.Split('\n'))
        {
            if (header && _usingDirective.IsMatch(line))
            {
                usings.Add(line);
            }
            else if (!(header && line.Length == 0))
            {
                header &= line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith('#');
                rest.Add(line);
            }
        }

        return string.Join('\n', usings.Order(StringComparer.Ordinal)) + "\n\n" + string.Join('\n', rest);
    }

    private static int OrderOf(Dictionary<string, int> drawn, string guid)
    {
        if (!drawn.TryGetValue(guid, out int order))
        {
            order = drawn.Count + 1;
            drawn[guid] = order;
        }

        return order;
    }
}

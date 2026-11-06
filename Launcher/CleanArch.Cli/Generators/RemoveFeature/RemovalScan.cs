using System.Text;
using System.Text.RegularExpressions;
using CleanArch.Cli.Generators.Shared;

namespace CleanArch.Cli.Generators.RemoveFeature;

internal static class RemovalScan
{
    internal static readonly Regex _declaration = new(@"\b(?:(?:record\s+)?(?:class|struct)|interface|record|enum)\s+([A-Z]\w*)", RegexOptions.None, TimeSpan.FromSeconds(1));

    internal static readonly Regex _sectionName = new(@"\bconst\s+string\s+SectionName\s*=\s*""([A-Z][A-Za-z0-9]*)""", RegexOptions.None, TimeSpan.FromSeconds(1));

    internal static readonly HashSet<string> _skippedFolders = new(StringComparer.Ordinal)
    {
        ".git", ".vs", ".claude", "bin", "obj", "artifacts", "node_modules", "TestResults", "Generated", "Migrations", FeatureManifest.Folder,
    };

    internal static List<string> FeatureFolders(List<string> files, string features)
    {
        string marker = $"/Features/{features}/";

        return [.. files
            .Select(file => file[..(file.IndexOf(marker, StringComparison.Ordinal) + marker.Length - 1)])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    internal static List<string> DiscoverFolders(string root, string features)
    {
        var folders = new List<string>();

        foreach (string top in new[] { "Src", "Tests" })
        {
            string start = Path.Combine(root, top);

            if (!Directory.Exists(start))
            {
                continue;
            }

            foreach (string file in Walk(root, start, "*"))
            {
                string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                string marker = $"/Features/{features}/";
                int at = relative.IndexOf(marker, StringComparison.Ordinal);

                if (at >= 0)
                {
                    folders.Add(relative[..(at + marker.Length - 1)]);
                }
            }
        }

        return [.. folders.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    internal static IEnumerable<string> Walk(string root, string directory, string pattern)
    {
        foreach (string file in Directory.EnumerateFiles(directory, pattern))
        {
            yield return file;
        }

        foreach (string child in Directory.EnumerateDirectories(directory))
        {
            bool tools = Path.GetFileName(child) == "Tools" && Path.GetFullPath(Path.GetDirectoryName(child)!) == Path.GetFullPath(root);

            if (!_skippedFolders.Contains(Path.GetFileName(child)) && !tools)
            {
                foreach (string file in Walk(root, child, pattern))
                {
                    yield return file;
                }
            }
        }
    }

    internal static string UpperSnake(string pascal) => CSharpNames.Snake(pascal).ToUpperInvariant();

    internal static List<string> DeclaredTypes(string root, Manifest manifest, string type)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in manifest.Files.Where(entry => entry.Path.EndsWith(".cs", StringComparison.Ordinal) && !manifest.Shared.Contains(entry.Path)))
        {
            string full = Path.Combine(root, entry.Path);

            if (!File.Exists(full))
            {
                continue;
            }

            foreach (Match match in _declaration.Matches(File.ReadAllText(full)))
            {
                names.Add(match.Groups[1].Value);
            }
        }

        names.Remove(type);

        return [.. names.Order(StringComparer.Ordinal)];
    }

    internal static List<string> Sections(string root, Manifest manifest)
    {
        var sections = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in manifest.Files.Where(entry => entry.Path.EndsWith("Options.cs", StringComparison.Ordinal) && entry.Path.StartsWith("Src/", StringComparison.Ordinal)))
        {
            string full = Path.Combine(root, entry.Path);

            if (File.Exists(full) && _sectionName.Match(File.ReadAllText(full)) is { Success: true } match)
            {
                sections.Add(match.Groups[1].Value);
            }
        }

        return [.. sections.Order(StringComparer.Ordinal)];
    }

    internal static string Code(string line)
    {
        string trimmed = line.TrimStart();

        if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("/*", StringComparison.Ordinal) || trimmed.StartsWith('*'))
        {
            return string.Empty;
        }

        var code = new StringBuilder(line.Length);
        int i = 0;

        while (i < line.Length)
        {
            char c = line[i];

            if (c == '/' && i + 1 < line.Length && line[i + 1] == '/')
            {
                break;
            }

            if (c == '\'')
            {
                int close = line.IndexOf('\'', i + (i + 1 < line.Length && line[i + 1] == '\\' ? 3 : 2));

                i = close < 0 ? i + 1 : close + 1;

                continue;
            }

            if (c is '"' or '@' or '$')
            {
                int start = i;
                bool verbatim = false;

                while (i < line.Length && line[i] is '@' or '$')
                {
                    verbatim |= line[i] == '@';
                    i++;
                }

                if (i >= line.Length || line[i] != '"')
                {
                    code.Append(line, start, i - start);

                    continue;
                }

                int quotes = 0;

                while (i + quotes < line.Length && line[i + quotes] == '"')
                {
                    quotes++;
                }

                if (quotes >= 3)
                {
                    int end = line.IndexOf(new string('"', quotes), i + quotes, StringComparison.Ordinal);

                    i = end < 0 ? line.Length : end + quotes;
                }
                else
                {
                    i++;

                    while (i < line.Length)
                    {
                        if (!verbatim && line[i] == '\\')
                        {
                            i += 2;
                        }
                        else if (verbatim && line[i] == '"' && i + 1 < line.Length && line[i + 1] == '"')
                        {
                            i += 2;
                        }
                        else if (line[i] == '"')
                        {
                            i++;

                            break;
                        }
                        else
                        {
                            i++;
                        }
                    }
                }

                code.Append("\"\"");

                continue;
            }

            code.Append(c);
            i++;
        }

        return code.ToString();
    }

    internal static bool Outside(string relative, List<string> folders, HashSet<string> own) =>
        !own.Contains(relative) && !folders.Any(folder => relative.StartsWith(folder + "/", StringComparison.Ordinal));

    internal static List<Hit> ScanCode(string root, string features, string? type, List<string> declared, List<string> folders, HashSet<string> own, Func<string, string?[]?> read)
    {
        var composition = new List<Regex>
        {
            FeatureRemoval.Rx($@"\bAdd{features}(Feature)?\b"),
            FeatureRemoval.Rx($@"nameof\(\w+\.Add{features}\)"),
        };

        var named = FeatureRemoval.Rx($@"AddFeature\(\s*""{features}""");
        var model = new List<Regex> { FeatureRemoval.Rx($@"\b{features}Schema\b") };
        var reference = new List<Regex> { FeatureRemoval.Rx($@"\.Features\.{features}\b") };
        Regex? typedRegistration = null;
        Regex? declaredName = null;

        if (type is not null)
        {
            model.Add(FeatureRemoval.Rx($@"\b{type}Record(Configuration)?\b"));

            if (declared.Count > 0)
            {
                string names = string.Join("|", declared.Select(Regex.Escape));

                declaredName = FeatureRemoval.Rx($@"\b(?:{names})\b");
                typedRegistration = FeatureRemoval.Rx($@"\b(Try)?Add\w*\s*[<(].*\b(?:{names})\b");
            }
        }

        var hits = new List<Hit>();

        foreach (string top in new[] { "Src", "Tests" })
        {
            string start = Path.Combine(root, top);

            if (!Directory.Exists(start))
            {
                continue;
            }

            foreach (string file in Walk(root, start, "*.cs"))
            {
                string relative = Path.GetRelativePath(root, file).Replace('\\', '/');

                if (!Outside(relative, folders, own) || read(relative) is not { } lines)
                {
                    continue;
                }

                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i] is not { } line)
                    {
                        continue;
                    }

                    string code = Code(line);

                    HitKind? kind = composition.Any(pattern => pattern.IsMatch(code)) || named.IsMatch(line) || (typedRegistration?.IsMatch(code) ?? false) ? HitKind.Composition
                        : model.Any(pattern => pattern.IsMatch(code)) ? HitKind.Model
                        : reference.Any(pattern => pattern.IsMatch(code)) || (declaredName?.IsMatch(code) ?? false) ? HitKind.Reference
                        : null;

                    if (kind is not null)
                    {
                        hits.Add(new Hit(kind.Value, relative, i + 1, line.Trim()));
                    }
                }
            }
        }

        return hits;
    }

    internal static List<Note> ScanNotes(string root, string features, string type, List<string> sections, List<string> folders, HashSet<string> own, Func<string, string?[]?> read)
    {
        const string Configuration = "Configuration";
        const string Documentation = "Documentation and samples";

        string kebab = CSharpNames.Kebab(features);
        var route = FeatureRemoval.Rx($@"/api/v1/{Regex.Escape(kebab)}(?![\w-])");
        var link = FeatureRemoval.Rx($@"\]\([^)\s]*\bfeatures/{Regex.Escape(kebab)}\.md(#[^)\s]*)?\)");
        var code = FeatureRemoval.Rx($@"`{Regex.Escape(char.ToLowerInvariant(type[0]) + type[1..])}\.[A-Za-z]+`");
        var keys = sections.Select(section => FeatureRemoval.Rx($@"^\s*""{section}""\s*:")).ToList();
        var variables = sections.Select(section => FeatureRemoval.Rx($@"^\s*#?\s*{UpperSnake(section)}_[A-Z0-9_]*=")).ToList();
        var mapped = sections.Select(section => FeatureRemoval.Rx($@"\b{section}__")).ToList();
        var headings = sections.Select(section => $"### `{section}`").ToHashSet(StringComparer.Ordinal);
        var notes = new List<Note>();

        foreach (string file in Walk(root, root, "*"))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            string name = Path.GetFileName(relative);
            bool settings = name.StartsWith("appsettings", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal);
            bool environment = name.StartsWith(".env", StringComparison.Ordinal);
            bool compose = (name.StartsWith("docker-compose", StringComparison.Ordinal) || name.StartsWith("compose", StringComparison.Ordinal))
                && (name.EndsWith(".yml", StringComparison.Ordinal) || name.EndsWith(".yaml", StringComparison.Ordinal));
            bool markdown = name.EndsWith(".md", StringComparison.Ordinal);
            bool samples = name.EndsWith(".http", StringComparison.Ordinal);

            if (!(settings || environment || compose || markdown || samples) || !Outside(relative, folders, own) || read(relative) is not { } lines)
            {
                continue;
            }

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i] is not { } line)
                {
                    continue;
                }

                string? group =
                    settings && keys.Any(key => key.IsMatch(line)) ? Configuration
                    : environment && variables.Any(variable => variable.IsMatch(line)) ? Configuration
                    : compose && mapped.Any(key => key.IsMatch(line)) ? Configuration
                    : markdown && headings.Contains(line.TrimEnd()) ? Configuration
                    : markdown && (link.IsMatch(line) || route.IsMatch(line) || code.IsMatch(line)) ? Documentation
                    : samples && route.IsMatch(line) ? Documentation
                    : null;

                if (group is not null)
                {
                    notes.Add(new Note(group, relative, i + 1, line.Trim()));
                }
            }
        }

        return notes;
    }
}

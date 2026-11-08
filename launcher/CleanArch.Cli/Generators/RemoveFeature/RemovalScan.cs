using System.Text;
using System.Text.RegularExpressions;
using CleanArch.Cli.Generators.Shared;

namespace CleanArch.Cli.Generators.RemoveFeature;

internal static class RemovalScan
{
    private static readonly string[] _sourceTops = ["src", "tests"];

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

        foreach (string top in new[] { "src", "tests" })
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
            bool tools = Path.GetFileName(child) == "tools" && Path.GetFullPath(Path.GetDirectoryName(child)!) == Path.GetFullPath(root);

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

        foreach (var entry in manifest.Files.Where(entry => entry.Path.EndsWith("Options.cs", StringComparison.Ordinal) && entry.Path.StartsWith("src/", StringComparison.Ordinal)))
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

        return trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("/*", StringComparison.Ordinal) || trimmed.StartsWith('*')
            ? string.Empty
            : new CodeLine(line).Strip();
    }

    internal static bool Outside(string relative, List<string> folders, HashSet<string> own) =>
        !own.Contains(relative) && !folders.Any(folder => relative.StartsWith(folder + "/", StringComparison.Ordinal));

    internal static IEnumerable<string> CodeFiles(string root) =>
        _sourceTops
            .Select(top => Path.Combine(root, top))
            .Where(Directory.Exists)
            .SelectMany(start => Walk(root, start, "*.cs"));

    internal static List<Hit> ScanCode(string root, string features, string? type, List<string> declared, List<string> folders, HashSet<string> own, Func<string, string?[]?> read)
    {
        var patterns = new CodePatterns(features, type, declared);

        return
        [
            .. OutsideLines(root, CodeFiles(root), folders, own, read)
                .Select(line => (line, Kind: patterns.Classify(line.Text)))
                .Where(found => found.Kind is not null)
                .Select(found => new Hit(found.Kind!.Value, found.line.Relative, found.line.Number, found.line.Text.Trim())),
        ];
    }

    internal static List<Note> ScanNotes(string root, string features, string type, List<string> sections, List<string> folders, HashSet<string> own, Func<string, string?[]?> read)
    {
        var patterns = new NotePatterns(features, type, sections);

        return
        [
            .. OutsideLines(root, Walk(root, root, "*").Where(file => NoteFile.Of(file) is not null), folders, own, read)
                .Select(line => (line, Group: patterns.Group(NoteFile.Of(line.Relative)!, line.Text)))
                .Where(found => found.Group is not null)
                .Select(found => new Note(found.Group!, found.line.Relative, found.line.Number, found.line.Text.Trim())),
        ];
    }

    private static IEnumerable<(string Relative, int Number, string Text)> OutsideLines(
        string root, IEnumerable<string> files, List<string> folders, HashSet<string> own, Func<string, string?[]?> read)
    {
        foreach (string file in files)
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');

            if (!Outside(relative, folders, own) || read(relative) is not { } lines)
            {
                continue;
            }

            for (int index = 0; index < lines.Length; index++)
            {
                if (lines[index] is { } line)
                {
                    yield return (relative, index + 1, line);
                }
            }
        }
    }

    private sealed class CodeLine(string line)
    {
        private readonly StringBuilder _code = new(line.Length);

        private int _at;

        internal string Strip()
        {
            while (_at < line.Length && !StartsComment())
            {
                char character = line[_at];

                if (character == '\'')
                {
                    SkipCharacter();
                }
                else if (character is '"' or '@' or '$')
                {
                    String();
                }
                else
                {
                    _code.Append(character);
                    _at++;
                }
            }

            return _code.ToString();
        }

        private bool StartsComment() => line[_at] == '/' && _at + 1 < line.Length && line[_at + 1] == '/';

        private void SkipCharacter()
        {
            int close = line.IndexOf('\'', _at + (_at + 1 < line.Length && line[_at + 1] == '\\' ? 3 : 2));

            _at = close < 0 ? _at + 1 : close + 1;
        }

        private void String()
        {
            int start = _at;
            bool verbatim = false;

            while (_at < line.Length && line[_at] is '@' or '$')
            {
                verbatim |= line[_at] == '@';
                _at++;
            }

            if (_at >= line.Length || line[_at] != '"')
            {
                _code.Append(line, start, _at - start);

                return;
            }

            int quotes = Quotes();

            if (quotes >= 3)
            {
                int end = line.IndexOf(new string('"', quotes), _at + quotes, StringComparison.Ordinal);

                _at = end < 0 ? line.Length : end + quotes;
            }
            else
            {
                SkipQuoted(verbatim);
            }

            _code.Append("\"\"");
        }

        private int Quotes()
        {
            int quotes = 0;

            while (_at + quotes < line.Length && line[_at + quotes] == '"')
            {
                quotes++;
            }

            return quotes;
        }

        private void SkipQuoted(bool verbatim)
        {
            _at++;

            while (_at < line.Length)
            {
                if (Escape(verbatim) is var step and > 0)
                {
                    _at += step;

                    continue;
                }

                _at++;

                if (line[_at - 1] == '"')
                {
                    return;
                }
            }
        }

        private int Escape(bool verbatim) => (verbatim, line[_at]) switch
        {
            (false, '\\') => 2,
            (true, '"') when _at + 1 < line.Length && line[_at + 1] == '"' => 2,
            _ => 0,
        };
    }

    private sealed class CodePatterns
    {
        private readonly List<Regex> _composition;

        private readonly Regex _named;

        private readonly List<Regex> _model;

        private readonly Regex _reference;

        private readonly Regex? _typedRegistration;

        private readonly Regex? _declaredName;

        internal CodePatterns(string features, string? type, List<string> declared)
        {
            _composition = [FeatureRemoval.Rx($@"\bAdd{features}(Feature)?\b"), FeatureRemoval.Rx($@"nameof\(\w+\.Add{features}\)")];
            _named = FeatureRemoval.Rx($@"AddFeature\(\s*""{features}""");
            _model = [FeatureRemoval.Rx($@"\b{features}Schema\b"), .. type is null ? [] : new[] { FeatureRemoval.Rx($@"\b{type}Record(Configuration)?\b") }];
            _reference = FeatureRemoval.Rx($@"\.Features\.{features}\b");

            if (type is not null && declared.Count > 0)
            {
                string names = string.Join("|", declared.Select(Regex.Escape));

                _declaredName = FeatureRemoval.Rx($@"\b(?:{names})\b");
                _typedRegistration = FeatureRemoval.Rx($@"\b(Try)?Add\w*\s*[<(].*\b(?:{names})\b");
            }
        }

        internal HitKind? Classify(string line)
        {
            string code = Code(line);

            if (_composition.Any(pattern => pattern.IsMatch(code)) || _named.IsMatch(line) || (_typedRegistration?.IsMatch(code) ?? false))
            {
                return HitKind.Composition;
            }

            if (_model.Any(pattern => pattern.IsMatch(code)))
            {
                return HitKind.Model;
            }

            return _reference.IsMatch(code) || (_declaredName?.IsMatch(code) ?? false) ? HitKind.Reference : null;
        }
    }

    private sealed record NoteFile(bool Settings, bool Environment, bool Compose, bool Markdown, bool Samples)
    {
        internal static NoteFile? Of(string path)
        {
            string name = Path.GetFileName(path);
            var file = new NoteFile(
                name.StartsWith("appsettings", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal),
                name.StartsWith(".env", StringComparison.Ordinal),
                (name.StartsWith("docker-compose", StringComparison.Ordinal) || name.StartsWith("compose", StringComparison.Ordinal))
                    && (name.EndsWith(".yml", StringComparison.Ordinal) || name.EndsWith(".yaml", StringComparison.Ordinal)),
                name.EndsWith(".md", StringComparison.Ordinal),
                name.EndsWith(".http", StringComparison.Ordinal));

            return file.Settings || file.Environment || file.Compose || file.Markdown || file.Samples ? file : null;
        }
    }

    private sealed class NotePatterns
    {
        private const string _configuration = "Configuration";

        private const string _documentation = "Documentation and samples";

        private readonly Regex _route;

        private readonly Regex _link;

        private readonly Regex _code;

        private readonly List<Regex> _keys;

        private readonly List<Regex> _variables;

        private readonly List<Regex> _mapped;

        private readonly HashSet<string> _headings;

        internal NotePatterns(string features, string type, List<string> sections)
        {
            string kebab = CSharpNames.Kebab(features);

            _route = FeatureRemoval.Rx($@"/api/v1/{Regex.Escape(kebab)}(?![\w-])");
            _link = FeatureRemoval.Rx($@"\]\([^)\s]*\bfeatures/{Regex.Escape(kebab)}\.md(#[^)\s]*)?\)");
            _code = FeatureRemoval.Rx($@"`{Regex.Escape(char.ToLowerInvariant(type[0]) + type[1..])}\.[A-Za-z]+`");
            _keys = [.. sections.Select(section => FeatureRemoval.Rx($@"^\s*""{section}""\s*:"))];
            _variables = [.. sections.Select(section => FeatureRemoval.Rx($@"^\s*#?\s*{UpperSnake(section)}_[A-Z0-9_]*="))];
            _mapped = [.. sections.Select(section => FeatureRemoval.Rx($@"\b{section}__"))];
            _headings = sections.Select(section => $"### `{section}`").ToHashSet(StringComparer.Ordinal);
        }

        internal string? Group(NoteFile file, string line)
        {
            if (Configures(file, line))
            {
                return _configuration;
            }

            return Documents(file, line) ? _documentation : null;
        }

        private bool Configures(NoteFile file, string line) =>
            (file.Settings && _keys.Any(key => key.IsMatch(line)))
            || (file.Environment && _variables.Any(variable => variable.IsMatch(line)))
            || (file.Compose && _mapped.Any(key => key.IsMatch(line)))
            || (file.Markdown && _headings.Contains(line.TrimEnd()));

        private bool Documents(NoteFile file, string line) =>
            (file.Markdown && (_link.IsMatch(line) || _route.IsMatch(line) || _code.IsMatch(line)))
            || (file.Samples && _route.IsMatch(line));
    }
}

using System.Text;
using System.Text.RegularExpressions;
using CleanArch.Cli.Generators.Shared;
using static CleanArch.Cli.Generators.Shared.ProjectFolders;
using static CleanArch.Cli.Generators.Shared.SourceText;

namespace CleanArch.Cli.Generators.AddSetting;

internal static class SettingEdits
{
    internal static (TextEdits.Editor Editor, List<string> Hosts) Edits(SettingSpec spec, string root, List<string> problems)
    {
        var editor = new TextEdits.Editor(root);
        var documents = new ProjectDocuments(root, "the setting", problems);

        RegisterOptions(editor, documents, spec, problems);
        var hosts = AddSections(editor, documents, spec, root, problems);

        if (spec.Keys.Any(key => key.IsRequired))
        {
            SupplyRequiredKeys(editor, documents, spec, root);
        }

        string guide = "docs/reference/configuration.md";

        Edit(editor, documents, guide, $"the {spec.Section} section of {guide}", text => AddGuide(text, spec));

        string vocabulary = $"Tests/Architecture/{FolderOf(spec.Prefix, "Architecture.Tests")}/Rules/LayoutConventionTests.cs";

        if (File.Exists(Path.Combine(root, vocabulary)))
        {
            Edit(editor, documents, vocabulary, $"the Configuration/Options folder word of {vocabulary}", text => AddVocabulary(text, FolderOf(spec.Prefix, "Application")));
        }

        return (editor, hosts);
    }

    private static void Edit(TextEdits.Editor editor, ProjectDocuments documents, string path, string step, Func<string, string?> edit)
    {
        documents.Load(path);
        editor.Step = step;
        editor.Rewrite(path, edit);
    }

    private static void RegisterOptions(TextEdits.Editor editor, ProjectDocuments documents, SettingSpec spec, List<string> problems)
    {
        string module = $"{SourceProject(spec.Prefix, "Application")}/ApplicationModule.cs";

        if (documents.Load(module).Contains(spec.Options, StringComparison.Ordinal))
        {
            problems.Add($"{module} already mentions {spec.Options}. A setting is written once; after that it is edited.");
        }

        Edit(editor, documents, module, $"the registration in Add{spec.Features} of {module}", text => Register(text, spec));
        editor.Step = $"the usings of {module}";

        foreach (string space in new[] { spec.Namespace, "Microsoft.Extensions.DependencyInjection.Extensions", "Microsoft.Extensions.Options" })
        {
            editor.Using(module, space);
        }
    }

    private static List<string> AddSections(TextEdits.Editor editor, ProjectDocuments documents, SettingSpec spec, string root, List<string> problems)
    {
        var hosts = new List<string>();

        foreach (string host in ComposingHosts(root, spec.Features))
        {
            string relative = Path.GetRelativePath(root, Path.Combine(host, "appsettings.json")).Replace('\\', '/');

            hosts.Add(Path.GetFileName(host));
            Edit(editor, documents, relative, $"the {spec.Section} section of {relative}", text => AddSection(text, spec));
        }

        if (hosts.Count == 0)
        {
            problems.Add($"No host composes {spec.Features}: none of Src/Presentation/* calls Add{spec.Features}(), so no appsettings.json would carry the section. Register the feature in its host first, as docs/guides/adding-a-feature.md says.");
        }

        return hosts;
    }

    private static IEnumerable<string> ComposingHosts(string root, string features)
    {
        string presentation = Full(root, Presentation);
        string call = $".Add{features}(";

        return Directory.Exists(presentation)
            ? Directory.EnumerateDirectories(presentation)
                .Order(StringComparer.Ordinal)
                .Where(host => SourceFiles(host, "*.cs").Any(file => File.ReadAllText(file).Contains(call, StringComparison.Ordinal)))
            : [];
    }

    private static void SupplyRequiredKeys(TextEdits.Editor editor, ProjectDocuments documents, SettingSpec spec, string root)
    {
        string composition = $"Tests/Architecture/{FolderOf(spec.Prefix, "Architecture.Tests")}/Composition/HostComposition.cs";

        if (File.Exists(Path.Combine(root, composition)))
        {
            Edit(editor, documents, composition, $"the ValidSettings of {composition}", text => AddValidSettings(text, spec));
        }

        if (File.Exists(Path.Combine(root, "docker-compose.yml")))
        {
            Edit(editor, documents, "docker-compose.yml", "the x-host-environment of docker-compose.yml", text => AddCompose(text, spec));
        }

        if (File.Exists(Path.Combine(root, ".env.example")))
        {
            Edit(editor, documents, ".env.example", "the variables of .env.example", text => AddEnvironment(text, spec));
        }
    }

    internal static string? Register(string text, SettingSpec spec)
    {
        var signature = new Regex($@"public static IServiceCollection Add{spec.Features}\(this IServiceCollection services[^)]*\)\s*\{{\n", RegexOptions.None, TimeSpan.FromSeconds(2)).Match(text);

        if (!signature.Success)
        {
            return null;
        }

        int position = signature.Index + signature.Length;
        int guards = 0;

        while (true)
        {
            int end = text.IndexOf('\n', position);

            if (end < 0 || !Regex.IsMatch(text[position..end], @"^\s+ArgumentNullException\.ThrowIfNull\(\w+\);$", RegexOptions.None, TimeSpan.FromSeconds(1)))
            {
                break;
            }

            position = end + 1;
            guards++;
        }

        if (guards == 0)
        {
            return null;
        }

        string bind = spec.TakesConfiguration
            ? $".Bind(configuration.GetSection({spec.Options}.SectionName))"
            : $".BindConfiguration({spec.Options}.SectionName)";
        string block =
            $"\n        services.AddOptions<{spec.Options}>()\n"
            + $"            {bind}\n"
            + "            .ValidateOnStart();\n"
            + $"        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<{spec.Options}>, {spec.Validator}>());\n";

        return text.Insert(position, block);
    }

    internal static string? AddValidSettings(string text, SettingSpec spec)
    {
        const string opening = "ValidSettings { get; } =\n        new Dictionary<string, string?>(StringComparer.Ordinal)\n        {\n";
        int start = text.IndexOf(opening, StringComparison.Ordinal);

        if (start < 0)
        {
            return null;
        }

        int close = text.IndexOf("\n        };\n", start + opening.Length - 1, StringComparison.Ordinal);

        if (close < 0 || text[start..close].Contains($"\"{spec.Section}:", StringComparison.Ordinal))
        {
            return null;
        }

        string entries = string.Concat(spec.Keys.Where(key => key.IsRequired).Select(key => $"            [\"{spec.Section}:{key.Name}\"] = \"{TestValue(key)}\",\n"));

        return text.Insert(close + 1, "\n" + entries);
    }

    internal static string TestValue(SettingKey key) => key switch
    {
        { Kind: KeyKind.Secret } => "architecture-tests-secret",
        { Min: <= 18, Max: >= 18 } => "architecture-tests",
        _ => new string('a', (int)key.Min),
    };

    internal static string? AddCompose(string text, SettingSpec spec)
    {
        string[] lines = text.Split('\n');
        int anchor = Array.FindIndex(lines, line => line == "x-host-environment: &host-environment");

        if (anchor < 0 || text.Contains($"{spec.Section}__", StringComparison.Ordinal))
        {
            return null;
        }

        int last = anchor;

        for (int index = anchor + 1; index < lines.Length && (lines[index].Length == 0 || lines[index].StartsWith(' ')); index++)
        {
            if (lines[index].Length > 0)
            {
                last = index;
            }
        }

        if (last == anchor)
        {
            return null;
        }

        string[] added =
        [
            string.Empty,
            .. spec.Keys.Where(key => key.IsRequired).Select(key => $"  {spec.Section}__{key.Name}: ${{{spec.Variable(key)}:?set {spec.Variable(key)} in .env}}"),
        ];

        return string.Join('\n', [.. lines[..(last + 1)], .. added, .. lines[(last + 1)..]]);
    }

    internal static string? AddEnvironment(string text, SettingSpec spec)
    {
        if (text.Length == 0 || spec.Keys.Any(key => key.IsRequired && text.Contains(spec.Variable(key) + "=", StringComparison.Ordinal)))
        {
            return null;
        }

        string entries = string.Concat(spec.Keys.Where(key => key.IsRequired).Select(key => $"{spec.Variable(key)}=\n"));

        return text.TrimEnd('\n') + $"\n\n# {spec.Section}: no default, so Compose refuses to start until each is set.\n" + entries;
    }

    internal static string? AddSection(string text, SettingSpec spec)
    {
        int close = text.LastIndexOf('}');

        if (close < 0 || text[..close].TrimEnd() is not { Length: > 0 } before || !text.TrimStart().StartsWith('{'))
        {
            return null;
        }

        var block = new StringBuilder();

        block.Append($"  \"{spec.Section}\": {{\n");
        block.Append(string.Join(",\n", spec.Keys.Select(key => $"    \"{key.Name}\": {key.JsonValue}")));
        block.Append("\n  }");

        string separator = before.EndsWith('{') || before.EndsWith(',') ? "\n" : ",\n";
        string result = before + separator + block + "\n" + text[close..];

        return NewSetting.TopLevelNames(result).Contains(spec.Section, StringComparer.Ordinal) ? result : null;
    }

    internal static string? AddGuide(string text, SettingSpec spec)
    {
        var table = new StringBuilder();

        table.Append($"### `{spec.Section}`\n\n| Key | Type | Default | Notes |\n|---|---|---|---|\n");

        foreach (var key in spec.Keys)
        {
            table.Append($"| `{key.Name}` | {key.Type} | {key.DocumentedDefault} | {Notes(spec, key)} |\n");
        }

        string group = $"## {SettingPlan.Words(spec.Features)}";
        var heading = new Regex($@"^{Regex.Escape(group)}$", RegexOptions.Multiline, TimeSpan.FromSeconds(1)).Match(text);
        int next;

        if (heading.Success)
        {
            var following = new Regex(@"^## ", RegexOptions.Multiline, TimeSpan.FromSeconds(1)).Match(text, heading.Index + heading.Length);

            if (!following.Success)
            {
                return null;
            }

            next = following.Index;

            return text[..next].TrimEnd('\n') + "\n\n" + table.ToString().TrimEnd('\n') + "\n\n" + text[next..];
        }

        var beyond = new Regex(@"^## Beyond the sections$", RegexOptions.Multiline, TimeSpan.FromSeconds(1)).Match(text);

        return beyond.Success
            ? text[..beyond.Index] + group + "\n\n" + table + "\n" + text[beyond.Index..]
            : null;
    }

    internal static string Notes(SettingSpec spec, SettingKey key) => key.Kind switch
    {
        KeyKind.Secret => $"**Required, secret.** Supplied by the environment variable `{spec.Section}__{key.Name}` (`{spec.Variable(key)}` in `.env` for Compose, `dotnet user-secrets` locally); the file keeps it empty and a blank value stops the host.",
        KeyKind.Text when key.Default is null => $"**Required.** {key.Min}–{key.Max} characters, from the environment variable `{spec.Section}__{key.Name}` (`{spec.Variable(key)}` in `.env` for Compose); the file holds none and a blank value stops the host.",
        KeyKind.Text => $"{key.Min}–{key.Max} characters.",
        KeyKind.Duration => $"`{key.Bound(key.Min)}`–`{key.Bound(key.Max)}`.",
        KeyKind.Bool => "`true` or `false`.",
        _ => $"{key.Bound(key.Min)}–{key.Bound(key.Max)}.",
    };

    internal static string? AddVocabulary(string text, string applicationFolder)
    {
        var start = new Regex($@"\[""Src/Application/{Regex.Escape(applicationFolder)}""\]\s*=\s*\[\s*\n", RegexOptions.None, TimeSpan.FromSeconds(2)).Match(text);

        if (!start.Success)
        {
            return null;
        }

        int close = text.IndexOf("],", start.Index + start.Length, StringComparison.Ordinal);

        if (close < 0)
        {
            return null;
        }

        string list = text[(start.Index + start.Length)..close];

        if (list.Contains("\"Configuration/Options\"", StringComparison.Ordinal))
        {
            return text;
        }

        string[] lines = list.Split('\n');
        int index = Array.FindIndex(lines, line => Regex.Match(line, @"""([^""]+)""", RegexOptions.None, TimeSpan.FromSeconds(1)) is { Success: true } first
            && string.CompareOrdinal(first.Groups[1].Value, "Configuration/Options") > 0);
        int at = start.Index + start.Length;

        if (index < 0)
        {
            return null;
        }

        at += string.Join('\n', lines[..index]).Length + (index > 0 ? 1 : 0);

        string indent = new([.. lines[index].TakeWhile(char.IsWhiteSpace)]);

        return text.Insert(at, $"{indent}\"Configuration/Options\",\n");
    }
}

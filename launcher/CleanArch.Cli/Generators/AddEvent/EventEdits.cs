using System.Text.RegularExpressions;
using CleanArch.Cli.Generators.Shared;
using static CleanArch.Cli.Generators.Shared.ProjectFolders;

namespace CleanArch.Cli.Generators.AddEvent;

internal static class EventEdits
{
    internal static TextEdits.Editor Edits(EventSpec spec, string root, List<string> problems)
    {
        var editor = new TextEdits.Editor(root);
        var documents = new ProjectDocuments(root, "the event", problems);

        if (spec.ConsumerFeatures is not null)
        {
            RegisterConsumer(editor, documents, spec, problems);
        }
        else
        {
            ListUnconsumed(editor, documents, spec, root, problems);
        }

        return editor;
    }

    private static void RegisterConsumer(TextEdits.Editor editor, ProjectDocuments documents, EventSpec spec, List<string> problems)
    {
        string module = $"{SourceProject(spec.Prefix, "Application")}/ApplicationModule.cs";

        if (documents.Load(module).Contains(spec.ConsumerName!, StringComparison.Ordinal))
        {
            problems.Add($"{module} already mentions {spec.ConsumerName}. A consumer is registered once.");
        }

        editor.Step = $"the registration in Add{spec.ConsumerFeatures} of {module}";
        editor.Rewrite(module, text => Register(text, spec));
        editor.Step = $"the usings of {module}";

        foreach (string space in new[] { $"{spec.Prefix}.Application.Core", spec.EventNamespace, spec.ConsumerNamespace })
        {
            editor.Using(module, space);
        }
    }

    private static void ListUnconsumed(TextEdits.Editor editor, ProjectDocuments documents, EventSpec spec, string root, List<string> problems)
    {
        string list = $"tests/Architecture/{FolderOf(spec.Prefix, "Architecture.Tests")}/Rules/DomainEventTests.cs";

        if (!File.Exists(Path.Combine(root, list)))
        {
            problems.Add($"{list} is not there: it holds the closed list of events nobody consumes, which an unconsumed event joins.");

            return;
        }

        documents.Load(list);
        editor.Step = $"the _deliberatelyUnconsumed list of {list}";
        editor.Rewrite(list, text => Excuse(text, spec));
    }

    private static string? Register(string text, EventSpec spec)
    {
        var method = new Regex($@"public static IServiceCollection Add{spec.ConsumerFeatures}\(this IServiceCollection services[^)]*\)\s*\{{\n", RegexOptions.None, TimeSpan.FromSeconds(2)).Match(text);

        if (!method.Success)
        {
            return null;
        }

        int start = method.Index + method.Length;
        int next = text.IndexOf("\n    public static ", start, StringComparison.Ordinal);
        int end = text.IndexOf("        return services;\n", start, StringComparison.Ordinal);

        if (end < 0 || (next >= 0 && end > next) || !text[..end].EndsWith("\n\n", StringComparison.Ordinal))
        {
            return null;
        }

        return text.Insert(end, $"        services.AddDomainEventConsumer<{spec.Name}, {spec.ConsumerName}>();\n\n");
    }

    private static string? Excuse(string text, EventSpec spec)
    {
        const string opening = "private static readonly string[] _deliberatelyUnconsumed =\n    [\n";
        int start = text.IndexOf(opening, StringComparison.Ordinal);

        if (start < 0)
        {
            return null;
        }

        int close = text.IndexOf("\n    ];\n", start + opening.Length - 1, StringComparison.Ordinal);

        if (close < 0 || text[start..close].Contains($"\"{spec.Name}\"", StringComparison.Ordinal))
        {
            return null;
        }

        return text.Insert(close + 1, $"        // {spec.Unconsumed}\n        \"{spec.Name}\",\n");
    }
}

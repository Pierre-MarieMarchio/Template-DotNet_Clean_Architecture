using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class DocumentationFrontmatterTests
{
    private const string _delimiter = "---";

    private const string _markerOpen = "<!--" + "#" + "if (files)-->";

    private const string _markerClose = "<!--" + "#" + "endif-->";

    private static readonly string[] _types = ["decision", "guide", "reference", "concept", "feature", "template"];

    private static readonly (string Folder, string Type)[] _typeOfFolder =
    [
        /*#if (templateMaintenance)*/
        ("template/decisions/", "decision"),
        ("template/", "guide"),
        /*#endif*/
        ("_page-templates/", "template"),
        ("getting-started/", "guide"),
        ("guides/", "guide"),
        ("architecture/", "concept"),
        ("reference/", "reference"),
        ("features/", "feature"),
        ("decisions/", "decision"),
    ];

    private static readonly string[] _reservedNames = ["index.md", "log.md"];

    private static readonly string[] _skippedDirectories = ["plans"];

    private static readonly Regex _field = new(
        @"^(?<key>[a-z_]+): (?<value>.*)$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _marker = new(
        @"#(?:if|elif|else|endif)\b",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _statusLine = new(
        @"^Status: (?<state>\S+?)(?:[;,.\s]|$)",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture | RegexOptions.Multiline,
        TimeSpan.FromSeconds(1));

    [Fact]
    public void EveryPageUnderDocs_OpensWithAConformantFrontmatter()
    {
        string root = ProjectReferenceGraph.RepositoryRoot;
        string docs = Path.Combine(root, "docs");

        Directory.Exists(docs).ShouldBeTrue("this project keeps its pages under docs/, which the rule reads.");

        string[] problems = [.. Pages(docs)
            .Select(page => Path.GetRelativePath(docs, page).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .SelectMany(page => Problems(page, File.ReadAllText(Path.Combine(docs, page))).Select(problem => $"docs/{page}: {problem}"))];

        problems.ShouldBeEmpty(
            "every page under docs/ opens with a frontmatter at its first byte: type, title and description, "
            + "and status on a decision; docs/README.md#adding-a-page says what each field holds.");
    }

    [Fact]
    public void TheRule_RefusesEachDefect_AndAcceptsAConformantPage()
    {
        const string page = "guides/a.md";
        const string decision = "decisions/1000-a.md";
        const string good = "---\ntype: guide\ntitle: A task\ndescription: \"How to do it: in one sentence.\"\n---\n\n# A task\n\nBody.\n";
        const string goodDecision = "---\ntype: decision\ntitle: 1000. A choice\ndescription: Why.\nstatus: Superseded\n---\n\n# 1000. A choice\n\nStatus: Superseded by [1001](1001-b.md)\n";

        Problems(page, good).ShouldBeEmpty();
        Problems(decision, goodDecision).ShouldBeEmpty();
        Problems("guides/index.md", "# Guides\n").ShouldBeEmpty();
        Problems("guides/README.md", good.Replace("type: guide", "type: reference", StringComparison.Ordinal)).ShouldBeEmpty();

        Problems(page, "# A task\n").ShouldNotBeEmpty();
        Problems(page, "\n" + good).ShouldNotBeEmpty();
        Problems(page, good.Replace("---\n\n#", "\n#", StringComparison.Ordinal)).ShouldNotBeEmpty();
        Problems(page, good.Replace("type: guide", "type: tutorial", StringComparison.Ordinal)).ShouldNotBeEmpty();
        Problems(page, good.Replace("type: guide", "type: concept", StringComparison.Ordinal)).ShouldNotBeEmpty();
        Problems(page, good.Replace("title: A task", "title: Another task", StringComparison.Ordinal)).ShouldNotBeEmpty();
        Problems(page, good.Replace("description: \"How to do it: in one sentence.\"\n", string.Empty, StringComparison.Ordinal)).ShouldNotBeEmpty();
        Problems(page, good.Replace("description: \"How to do it: in one sentence.\"", "description: ", StringComparison.Ordinal)).ShouldNotBeEmpty();
        Problems(page, good.Replace("title: A task\n", $"title: A task\n{_markerOpen}\ntags: [files]\n{_markerClose}\n", StringComparison.Ordinal)).ShouldNotBeEmpty();
        Problems(page, good.Replace("\n\n# A task", $"\n{_markerOpen}\n# A task", StringComparison.Ordinal)).ShouldNotBeEmpty();
        Problems(decision, goodDecision.Replace("status: Superseded\n", string.Empty, StringComparison.Ordinal)).ShouldNotBeEmpty();
        Problems(decision, goodDecision.Replace("status: Superseded", "status: Accepted", StringComparison.Ordinal)).ShouldNotBeEmpty();
        Problems(decision, goodDecision.Replace("Status: Superseded by [1001](1001-b.md)\n", string.Empty, StringComparison.Ordinal)).ShouldNotBeEmpty();
    }

    private static List<string> Problems(string page, string text)
    {
        if (_reservedNames.Contains(Path.GetFileName(page), StringComparer.Ordinal))
        {
            return [];
        }

        string[] lines = text.Split('\n');

        if (lines[0] != _delimiter)
        {
            return ["does not open with '---' on its first line, so it has no frontmatter."];
        }

        int closing = Array.IndexOf(lines, _delimiter, 1);

        if (closing < 0)
        {
            return ["opens a frontmatter that no '---' line closes."];
        }

        var problems = new List<string>();
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string line in lines[1..closing])
        {
            if (_marker.IsMatch(line))
            {
                problems.Add($"holds a template marker inside its frontmatter ('{line}'): a frontmatter holds in every generated form, so markers go after it.");
            }
            else if (_field.Match(line) is { Success: true } field)
            {
                fields[field.Groups["key"].Value] = Unquoted(field.Groups["value"].Value.Trim());
            }
            else
            {
                problems.Add($"holds a frontmatter line that is not 'key: value' ('{line}').");
            }
        }

        foreach (string required in new[] { "type", "title", "description" })
        {
            if (!fields.TryGetValue(required, out string? value) || value.Length == 0)
            {
                problems.Add($"its frontmatter has no {required}.");
            }
        }

        string type = fields.GetValueOrDefault("type", string.Empty);

        if (type.Length > 0 && !_types.Contains(type, StringComparer.Ordinal))
        {
            problems.Add($"its type '{type}' is not one of {string.Join(", ", _types)}.");
        }
        else if (Path.GetFileName(page) != "README.md"
            && _typeOfFolder.FirstOrDefault(entry => page.StartsWith(entry.Folder, StringComparison.Ordinal)) is { Type: not null } folder
            && type.Length > 0
            && type != folder.Type)
        {
            problems.Add($"its type is '{type}', where every page under docs/{folder.Folder} is a {folder.Type}.");
        }

        string? heading = lines[(closing + 1)..].FirstOrDefault(line => line.Trim().Length > 0);

        if (heading is null || !heading.StartsWith("# ", StringComparison.Ordinal))
        {
            problems.Add($"the first line after its frontmatter is not its '# ' heading ('{heading}').");
        }
        else if (fields.TryGetValue("title", out string? title) && title.Length > 0 && heading[2..] != title)
        {
            problems.Add($"its title '{title}' is not its heading '{heading[2..]}'.");
        }

        string body = string.Join('\n', lines[(closing + 1)..]);
        string? state = _statusLine.Match(body) is { Success: true } status ? status.Groups["state"].Value : null;
        bool stated = fields.TryGetValue("status", out string? declared);

        if (type == "decision" && !stated)
        {
            problems.Add("is a decision whose frontmatter has no status.");
        }

        if (type == "decision" && state is null)
        {
            problems.Add("is a decision whose body has no 'Status: ' line.");
        }

        if (stated && state is not null && declared != state)
        {
            problems.Add($"its status '{declared}' is not the state its 'Status: ' line opens with, '{state}'.");
        }

        return problems;
    }

    private static string Unquoted(string value) =>
        value.Length >= 2 && value[0] == '"' && value[^1] == '"' ? value[1..^1] : value;

    private static IEnumerable<string> Pages(string directory) =>
        Directory.EnumerateFiles(directory, "*.md")
            .Concat(Directory.EnumerateDirectories(directory)
                .Where(child => !_skippedDirectories.Contains(Path.GetFileName(child), StringComparer.Ordinal))
                .SelectMany(Pages));
}

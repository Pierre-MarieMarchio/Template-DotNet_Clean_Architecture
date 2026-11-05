using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class DocumentationCitationTests
{
    private const string _agentFileName = "AGENTS.md";

    private static readonly string[] _rootPages = [_agentFileName, "README.md", "CONTRIBUTING.md", "SECURITY.md"];

    private static readonly string[] _exemptPages = ["CHANGELOG.md"];

    private static readonly string[] _pagesWithoutCitations =
    [
        /*#if (templateMaintenance)*/
        "docs/template/",
        /*#endif*/
        "docs/_page-templates/",
    ];

    private const string _taskTablePage = "docs/guides/build-and-ci.md";

    private const string _taskTableHeading = "## The task launcher";

    private static readonly string[] _tasksOutsideTheTable =
    [
        "help",
        /*#if (templateMaintenance)*/
        "web-lockfiles",
        /*#endif*/
    ];

    private static readonly string[] _skippedDirectories = ["bin", "obj", "node_modules", "plans"];

    private static readonly string[] _keptDotDirectories = [".github", ".template.config"];

    private static readonly Regex _taskCitation = new(
        @"dotnet run Tools/Tasks\.cs(?:[ \t]+--)?[ \t]+(?<task>[a-z][a-z-]*)(?<rest>[^`\n#]*)",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _citedOption = new(
        @"(?<![\w-])--(?<option>[a-z][a-z-]*)",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _knownTasks = new(
        @"_knownTasks\s*=\s*\[(?<body>[^\]]*)\]",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _taskOptionNames = new(
        @"_(?:webJoin|upgrade)OptionNames\s*=\s*\[(?<body>[^\]]*)\]",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _parsedOption = new(
        @"(?:argument is|arguments is \[)\s*""--(?<option>[a-z][a-z-]*)""",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _generatorOption = new(
        @"\[""--(?<option>[a-z][a-z-]*)""\]\s*=",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _quotedName = new(
        @"""(?:--)?(?<name>[a-z][a-z-]*)""",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _ownerAccess = new(
        @"--access[ \t]+owner(?![\w-])",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _codeSpan = new(
        @"`(?<span>[^`\n]+)`",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _rootedPath = new(
        @"^(?:Src|Tests|Tools|docs|\.github|\.config)/[\w\-./]+$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _bareFileName = new(
        @"^[\w\-.]+\.(?:sln|slnx|props|json|ya?ml|md|ps1|sh|http|runsettings|minimum|csproj|py|cs)$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _link = new(
        @"\]\((?<target>[^)\s]+)\)",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _heading = new(
        @"^#{1,6}[ \t]+(?<title>.+?)[ \t]*$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _notSlugCharacter = new(
        @"[^\p{L}\p{Nd} _-]",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    [Fact]
    public void EveryCitedTask_IsATaskOfTheLauncher()
    {
        var tasks = LauncherTasks();
        var citations = TaskCitations();

        tasks.ShouldContain("verify", "the launcher's task list was not found in Tools/Tasks.cs.");
        citations.ShouldNotBeEmpty("the pages cite at least the gate; a scan that finds no task is broken.");

        citations
            .Where(citation => !tasks.Contains(citation.Task))
            .Select(citation => $"{citation.Page}: {citation.Task}")
            .Distinct(StringComparer.Ordinal)
            .ShouldBeEmpty(
                "A page cites a task Tools/Tasks.cs does not have, so the command it gives fails. "
                + "Cite a task `dotnet run Tools/Tasks.cs help` lists, or add the task.");
    }

    [Fact]
    public void EveryTaskOfTheLauncher_HasItsRowInTheTaskTable()
    {
        var listed = TaskTableTasks(File.ReadAllText(Path.Combine(ProjectReferenceGraph.RepositoryRoot, _taskTablePage)));

        listed.ShouldContain("verify", $"the task table of {_taskTablePage} was not found under '{_taskTableHeading}'.");

        LauncherTasks()
            .Where(task => !_tasksOutsideTheTable.Contains(task, StringComparer.Ordinal))
            .Where(task => task != "sonar" || SonarRecorded())
            .Where(task => !listed.Contains(task))
            .Order(StringComparer.Ordinal)
            .ShouldBeEmpty(
                $"`dotnet run Tools/Tasks.cs help` lists a task the table under '{_taskTableHeading}' in {_taskTablePage} "
                + "has no row for, so a reader of the reference list never learns it exists. Add its row, "
                + "conditioned as the task is, with a code span naming it in the first column.");
    }

    [Fact]
    public void TheTaskTableScan_ReadsTheFirstColumnOfItsSectionAlone()
    {
        string opening = "<!--" + "#" + "if (api)-->";
        string closing = "<!--" + "#" + "endif-->";
        string page = string.Join(
            "\n",
            "## Before",
            "| `ignored` | a row of another section |",
            _taskTableHeading,
            "| Task | Runs |",
            "|---|---|",
            $"| {opening} |",
            "| `run`, `test --no-integration` | runs `skipped` |",
            $"| {closing} |",
            "## After",
            "| `after` | a row of another section |");

        TaskTableTasks(page).Order(StringComparer.Ordinal).ShouldBe(["run", "test"]);
    }

    [Fact]
    public void EveryCitedOption_IsAnOptionOfTheLauncher()
    {
        var options = LauncherOptions();
        var citations = TaskCitations();

        options.ShouldContain("no-integration", "the launcher's options were not found in Tools/Tasks.cs.");

        citations
            .SelectMany(citation => _citedOption.Matches(citation.Rest)
                .Select(match => match.Groups["option"].Value)
                .Where(option => !options.Contains(option))
                .Select(option => $"{citation.Page}: {citation.Task} --{option}"))
            .Distinct(StringComparer.Ordinal)
            .ShouldBeEmpty(
                "A page cites an option Tools/Tasks.cs does not accept, so the command it gives is refused. "
                + "Cite an option `dotnet run Tools/Tasks.cs help` lists.");
    }

    [Fact]
    public void EveryCitedOwnerFeature_NamesTheFateOfItsRows()
    {
        var owned = TaskCitations()
            .Where(citation => citation.Task == "new-feature" && _ownerAccess.IsMatch(citation.Rest))
            .ToList();

        owned
            .Where(citation => !citation.Rest.Contains("--deletion", StringComparison.Ordinal))
            .Select(citation => $"{citation.Page}: new-feature{citation.Rest}")
            .ShouldBeEmpty(
                "new-feature refuses --access owner without --deletion delete|anonymise, so this command "
                + "fails as written. Add the --deletion the feature needs.");
    }

    [Fact]
    public void EveryPathTheDocumentationCites_Exists()
    {
        var names = NamesInTree();
        var pages = PathCitingPages().ToDictionary(Relative, File.ReadAllText, StringComparer.Ordinal);

        var (missing, checkedPaths) = MissingPaths(
            pages,
            path => Exists(Path.Combine(ProjectReferenceGraph.RepositoryRoot, path)),
            names.Contains);

        pages.Keys.ShouldContain(
            page => page.StartsWith("docs/", StringComparison.Ordinal),
            "the documentation pages are read; a scan that finds none is broken.");
        checkedPaths.ShouldBeGreaterThan(0, "the pages cite paths; a scan that checks none is broken.");
        missing.ShouldBeEmpty(
            "A page cites a path that does not exist in this project, so whoever follows it is lost. "
            + "Correct the path, or write an illustration as a placeholder (`<Feature>`) or as prose. "
            + "A page a feature or an option conditions must cite only what that form keeps.");
    }

    [Fact]
    public void EveryLinkTheRootPagesAndAgentFilesHold_Resolves()
    {
        var broken = new List<string>();
        int checkedLinks = 0;

        foreach (string page in CitingPages())
        {
            foreach (Match link in _link.Matches(File.ReadAllText(page)))
            {
                string target = link.Groups["target"].Value;

                if (target.Contains("://", StringComparison.Ordinal) || target.StartsWith("mailto:", StringComparison.Ordinal))
                {
                    continue;
                }

                checkedLinks++;

                if (Unresolved(page, target) is { } problem)
                {
                    broken.Add($"{Relative(page)}: ({target}) {problem}");
                }
            }
        }

        checkedLinks.ShouldBeGreaterThan(0, "the agent files link the pages they rely on; a scan that checks none is broken.");
        broken.ShouldBeEmpty(
            "A root page or an AGENTS.md links a file or a heading that does not exist. Point the link "
            + "at the page as it is now, and the anchor at one of its headings.");
    }

    [Fact]
    public void TheMatchers_AreSensitive()
    {
        var citation = _taskCitation.Match("`dotnet run Tools/Tasks.cs -- build --configuration Release`");
        citation.Groups["task"].Value.ShouldBe("build");
        _citedOption.Matches(citation.Groups["rest"].Value).Single().Groups["option"].Value.ShouldBe("configuration");

        _taskCitation.Match("dotnet run Tools/Tasks.cs test --no-integration   # without Docker")
            .Groups["rest"].Value.Trim().ShouldBe("--no-integration");
        _taskCitation.IsMatch("dotnet run Tools/Tasks.cs <task>").ShouldBeFalse();
        _taskCitation.Match("dotnet run Tools/Tasks.cs end-to-end --keep-stack").Groups["task"].Value.ShouldBe("end-to-end");
        _quotedName.Match("\"end-to-end\",").Groups["name"].Value.ShouldBe("end-to-end");
        _citedOption.IsMatch("[--deletion delete|anonymise]").ShouldBeTrue();
        _citedOption.IsMatch("migration-add Add-Things").ShouldBeFalse();

        _ownerAccess.IsMatch(" --access owner --deletion delete").ShouldBeTrue();
        _ownerAccess.IsMatch(" --access owner|shared").ShouldBeTrue("owner is one of the values it offers");
        _ownerAccess.IsMatch(" --access shared").ShouldBeFalse();

        _rootedPath.IsMatch("Src/Web/packages/api-client/src/generated/").ShouldBeTrue();
        _rootedPath.IsMatch("Src/Web/apps/<App>").ShouldBeFalse();
        _bareFileName.IsMatch("coverage.minimum").ShouldBeTrue();
        _bareFileName.IsMatch("pnpm api").ShouldBeFalse();

        Slug("The closed lists a feature joins").ShouldBe("the-closed-lists-a-feature-joins");
        Slug("Files not edited by hand").ShouldBe("files-not-edited-by-hand");
        Slug("`ConnectionStrings:Default`").ShouldBe("connectionstringsdefault");
    }

    [Fact]
    public void ThePathScan_IsSensitive()
    {
        var pages = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["one.md"] = "See `Src/Present/File.cs`, `./Tests/Gone/File.cs`, `Missing.json` and `Src/Gone/`.",
            ["two.md"] = "A placeholder `Src/<Feature>/File.cs`, prose `Src/Gone/File.cs here`, "
                + "a name `Known.json` and a type `Item.Name` name nothing missing.",
        };

        var (missing, checkedPaths) = MissingPaths(
            pages,
            path => path == "Src/Present/File.cs",
            name => name == "Known.json");

        checkedPaths.ShouldBe(5);
        missing.ShouldBe(
            [
                "one.md: `Tests/Gone/File.cs`",
                "one.md: `Missing.json`, found nowhere in the tree",
                "one.md: `Src/Gone/`",
            ]);
    }

    private static (List<string> Missing, int Checked) MissingPaths(
        IReadOnlyDictionary<string, string> pages,
        Func<string, bool> pathExists,
        Func<string, bool> nameExists)
    {
        var missing = new List<string>();
        int checkedPaths = 0;

        foreach ((string page, string text) in pages)
        {
            foreach (Match span in _codeSpan.Matches(text))
            {
                string cited = span.Groups["span"].Value.Trim();
                cited = cited.StartsWith("./", StringComparison.Ordinal) ? cited[2..] : cited;

                if (_rootedPath.IsMatch(cited))
                {
                    checkedPaths++;

                    if (!pathExists(cited))
                    {
                        missing.Add($"{page}: `{cited}`");
                    }
                }
                else if (_bareFileName.IsMatch(cited))
                {
                    checkedPaths++;

                    if (!nameExists(cited))
                    {
                        missing.Add($"{page}: `{cited}`, found nowhere in the tree");
                    }
                }
            }
        }

        return (missing, checkedPaths);
    }

    private sealed record Citation(string Page, string Task, string Rest);

    private static HashSet<string> LauncherTasks()
    {
        string source = File.ReadAllText(Launcher());
        var tasks = QuotedNames(_knownTasks.Match(source).Groups["body"].Value);
        tasks.Add("help");

        return tasks;
    }

    private static HashSet<string> TaskTableTasks(string page)
    {
        var tasks = new HashSet<string>(StringComparer.Ordinal);
        bool inSection = false;

        foreach (string line in page.ReplaceLineEndings("\n").Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                inSection = line.TrimEnd() == _taskTableHeading;
            }
            else if (inSection && line.StartsWith('|') && line.Split('|') is [_, var firstCell, _, ..])
            {
                tasks.UnionWith(_codeSpan.Matches(firstCell).Select(span => span.Groups["span"].Value.Split(' ')[0]));
            }
        }

        return tasks;
    }

    private static bool SonarRecorded() =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(ProjectReferenceGraph.RepositoryRoot, "cleanarch.json")))?["options"]?["sonar"]?.GetValue<bool>() ?? false;

    private static HashSet<string> LauncherOptions()
    {
        string source = File.ReadAllText(Launcher());
        var options = _taskOptionNames.Matches(source)
            .SelectMany(match => QuotedNames(match.Groups["body"].Value))
            .ToHashSet(StringComparer.Ordinal);
        options.Add("help");

        foreach (Match parsed in _parsedOption.Matches(source).Concat(_generatorOption.Matches(source)))
        {
            options.Add(parsed.Groups["option"].Value);
        }

        return options;
    }

    private static HashSet<string> QuotedNames(string body) =>
        [.. _quotedName.Matches(body).Select(match => match.Groups["name"].Value)];

    private static string Launcher() => Path.Combine(ProjectReferenceGraph.RepositoryRoot, "Tools", "Tasks.cs");

    private static List<Citation> TaskCitations() =>
        [.. MarkdownPages()
            .Where(page => !_exemptPages.Contains(Path.GetFileName(page), StringComparer.Ordinal))
            .SelectMany(page => _taskCitation
                .Matches(File.ReadAllText(page).Replace("\\\n", " ", StringComparison.Ordinal))
                .Select(match => new Citation(Relative(page), match.Groups["task"].Value, match.Groups["rest"].Value)))];

    private static List<string> CitingPages() =>
        [.. _rootPages
            .Select(name => Path.Combine(ProjectReferenceGraph.RepositoryRoot, name))
            .Where(File.Exists)
            .Concat(MarkdownPages().Where(page => Path.GetFileName(page) == _agentFileName))
            .Distinct(StringComparer.Ordinal)];

    private static List<string> PathCitingPages() =>
        [.. CitingPages()
            .Concat(MarkdownPages().Where(page => Relative(page).StartsWith("docs/", StringComparison.Ordinal)
                && !_pagesWithoutCitations.Any(prefix => Relative(page).StartsWith(prefix, StringComparison.Ordinal))))
            .Distinct(StringComparer.Ordinal)];

    private static List<string> MarkdownPages() =>
        [.. TreeFiles(ProjectReferenceGraph.RepositoryRoot)
            .Where(file => file.EndsWith(".md", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];

    private static HashSet<string> NamesInTree() =>
        [.. TreeFiles(ProjectReferenceGraph.RepositoryRoot).Select(Path.GetFileName).OfType<string>()];

    private static IEnumerable<string> TreeFiles(string directory) =>
        Directory.EnumerateFiles(directory)
            .Concat(Directory.EnumerateDirectories(directory)
                .Where(child => Path.GetFileName(child) is var name
                    && !_skippedDirectories.Contains(name, StringComparer.Ordinal)
                    && (!name.StartsWith('.') || _keptDotDirectories.Contains(name, StringComparer.Ordinal)))
                .SelectMany(TreeFiles));

    private static string? Unresolved(string page, string target)
    {
        int hash = target.IndexOf('#', StringComparison.Ordinal);
        string file = hash < 0 ? target : target[..hash];
        string? anchor = hash < 0 ? null : target[(hash + 1)..];
        string resolved = file.Length == 0
            ? page
            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(page)!, Uri.UnescapeDataString(file)));

        if (!Exists(resolved))
        {
            return "names no file";
        }

        if (anchor is null || !resolved.EndsWith(".md", StringComparison.Ordinal))
        {
            return null;
        }

        return Anchors(resolved).Contains(anchor) ? null : $"names no heading of {Path.GetFileName(resolved)}";
    }

    private static HashSet<string> Anchors(string page)
    {
        var anchors = new HashSet<string>(StringComparer.Ordinal);
        bool fenced = false;

        foreach (string line in File.ReadLines(page))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                fenced = !fenced;
            }
            else if (!fenced && _heading.Match(line) is { Success: true } heading)
            {
                anchors.Add(Slug(heading.Groups["title"].Value));
            }
        }

        return anchors;
    }

    private static string Slug(string title) =>
        _notSlugCharacter.Replace(title.Trim().ToLowerInvariant(), string.Empty).Replace(' ', '-');

    private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    private static string Relative(string file) =>
        Path.GetRelativePath(ProjectReferenceGraph.RepositoryRoot, file).Replace('\\', '/');
}

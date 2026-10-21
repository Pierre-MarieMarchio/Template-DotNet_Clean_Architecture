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

    private static readonly string[] _skippedDirectories = ["bin", "obj", "node_modules", "plans"];

    private static readonly string[] _keptDotDirectories = [".github", ".template.config"];

    private static readonly Regex _taskCitation = new(
        @"dotnet run Tools/Tasks\.cs(?:[ \t]+--)?[ \t]+(?<task>[a-z][a-z0-9-]*)(?<rest>[^`\n#]*)",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _citedOption = new(
        @"(?<![\w-])--(?<option>[a-z][a-z-]*)",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _knownTasks = new(
        @"KnownTasks\s*=\s*\[(?<body>[^\]]*)\]",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _featureOptionNames = new(
        @"FeatureOptionNames\s*=\s*\[(?<body>[^\]]*)\]",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _parsedOption = new(
        @"(?:argument is|arguments is \[)\s*""--(?<option>[a-z][a-z-]*)""",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _quotedName = new(
        @"""(?:--)?(?<name>[a-z][a-z0-9-]*)""",
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
    public void EveryPathTheRootPagesAndAgentFilesCite_Exists()
    {
        var pages = CitingPages();
        var names = NamesInTree();
        var missing = new List<string>();
        int checkedPaths = 0;

        foreach (string page in pages)
        {
            string text = File.ReadAllText(page);

            foreach (Match span in _codeSpan.Matches(text))
            {
                string cited = span.Groups["span"].Value.Trim();
                cited = cited.StartsWith("./", StringComparison.Ordinal) ? cited[2..] : cited;

                if (_rootedPath.IsMatch(cited))
                {
                    checkedPaths++;

                    if (!Exists(Path.Combine(ProjectReferenceGraph.RepositoryRoot, cited)))
                    {
                        missing.Add($"{Relative(page)}: `{cited}`");
                    }
                }
                else if (_bareFileName.IsMatch(cited))
                {
                    checkedPaths++;

                    if (!names.Contains(cited))
                    {
                        missing.Add($"{Relative(page)}: `{cited}`, found nowhere in the tree");
                    }
                }
            }
        }

        pages.ShouldContain(page => Path.GetFileName(page) == _agentFileName, "the root AGENTS.md was not found.");
        checkedPaths.ShouldBeGreaterThan(0, "the agent files cite paths; a scan that checks none is broken.");
        missing.ShouldBeEmpty(
            "A root page or an AGENTS.md cites a path that does not exist, so whoever follows it is lost. "
            + "Correct the path, or write an illustration as a placeholder (`<Feature>`) or as prose.");
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
        _taskCitation.Match("dotnet run Tools/Tasks.cs e2e --keep-stack").Groups["task"].Value.ShouldBe("e2e");
        _quotedName.Match("\"e2e\",").Groups["name"].Value.ShouldBe("e2e");
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

    private sealed record Citation(string Page, string Task, string Rest);

    private static HashSet<string> LauncherTasks()
    {
        string source = File.ReadAllText(Launcher());
        var tasks = QuotedNames(_knownTasks.Match(source).Groups["body"].Value);
        tasks.Add("help");

        return tasks;
    }

    private static HashSet<string> LauncherOptions()
    {
        string source = File.ReadAllText(Launcher());
        var options = QuotedNames(_featureOptionNames.Match(source).Groups["body"].Value);
        options.Add("help");

        foreach (Match parsed in _parsedOption.Matches(source))
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

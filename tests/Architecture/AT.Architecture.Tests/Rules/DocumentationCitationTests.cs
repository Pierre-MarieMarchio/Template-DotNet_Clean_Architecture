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

    private const string _toolPage = "docs/reference/cli.md";

    private static readonly string[] _skippedDirectories = ["bin", "obj", "node_modules", "plans"];

    private static readonly string[] _keptDotDirectories = [".github", ".template.config"];

    private static readonly Regex _commandCitation = new(
        @"dotnet cleanarch (?<first>[a-z][a-z-]*)(?:[ \t]+(?<second>[a-z][a-z-]*))?(?<rest>[^`\n#]*)",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    private static readonly Regex _citedOption = new(
        @"(?<![\w-])--(?<option>[a-z][a-z-]*)",
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
        @"^(?:src|tests|tools|docs|\.github|\.config)/[\w\-./]+$",
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
    public void EveryCitedCommand_IsACommandOfTheProjectTool()
    {
        var commands = ToolCommands();
        var citations = CommandCitations(commands);

        commands.ShouldContain("verify", $"the command tables of {_toolPage} were not found.");
        citations.ShouldNotBeEmpty("the pages cite at least the gate; a scan that finds no command is broken.");

        citations
            .Where(citation => !commands.Contains(citation.Command))
            .Select(citation => $"{citation.Page}: {citation.Command}")
            .Distinct(StringComparer.Ordinal)
            .ShouldBeEmpty(
                $"A page cites a command {_toolPage} does not list, so the command it gives fails. "
                + "Cite a command `dotnet cleanarch help` lists, or add its row to the page.");
    }

    [Fact]
    public void EveryCitedOption_IsOneTheProjectToolPageNames()
    {
        string tool = File.ReadAllText(Path.Combine(ProjectReferenceGraph.RepositoryRoot, _toolPage));
        var citations = CommandCitations(ToolCommands());

        tool.ShouldContain("--check", Case.Sensitive, $"{_toolPage} was not read; a scan against nothing is broken.");

        citations
            .SelectMany(citation => _citedOption.Matches(citation.Rest)
                .Select(match => match.Groups["option"].Value)
                .Where(option => !tool.Contains($"--{option}", StringComparison.Ordinal))
                .Select(option => $"{citation.Page}: {citation.Command} --{option}"))
            .Distinct(StringComparer.Ordinal)
            .ShouldBeEmpty(
                $"A page cites an option {_toolPage} never names, so the command it gives is refused or the "
                + "page that lists the options is incomplete. Cite an option `dotnet cleanarch <command> --help` lists, "
                + "and name it in the page.");
    }

    [Fact]
    public void EveryCitedOwnerFeature_NamesTheFateOfItsRows()
    {
        var owned = CommandCitations(ToolCommands())
            .Where(citation => citation.Command == "add feature" && _ownerAccess.IsMatch(citation.Rest))
            .ToList();

        owned
            .Where(citation => !citation.Rest.Contains("--deletion", StringComparison.Ordinal))
            .Select(citation => $"{citation.Page}: add feature{citation.Rest}")
            .ShouldBeEmpty(
                "add feature refuses --access owner without --deletion delete|anonymise, so this command "
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
        var names = CommandsOfTables(string.Join(
            "\n",
            "| Command | Does |",
            "|---|---|",
            "| `dotnet cleanarch help`, `dotnet cleanarch --help` | lists |",
            "| `migrations add <Name>` | adds |",
            "| `migrations add --check` | checks |",
            "| `build`, `test --no-integration` | runs `skipped` |"));
        names.Order(StringComparer.Ordinal).ShouldBe(["build", "help", "migrations add", "test"]);

        var citation = CommandCitation("`dotnet cleanarch migrations add Add-Things --no-build`", names);
        citation.Command.ShouldBe("migrations add");
        _citedOption.Matches(citation.Rest).Single().Groups["option"].Value.ShouldBe("no-build");

        CommandCitation("dotnet cleanarch test --no-integration   # without Docker", names).Rest.Trim().ShouldBe("--no-integration");
        CommandCitation("dotnet cleanarch verify passes", names).Command.ShouldBe("verify");
        _commandCitation.IsMatch("dotnet cleanarch <command>").ShouldBeFalse();
        _commandCitation.IsMatch("dotnet cleanarch --help").ShouldBeFalse();
        _citedOption.IsMatch("[--deletion delete|anonymise]").ShouldBeTrue();
        _citedOption.IsMatch("migrations add Add-Things").ShouldBeFalse();

        _ownerAccess.IsMatch(" --access owner --deletion delete").ShouldBeTrue();
        _ownerAccess.IsMatch(" --access owner|shared").ShouldBeTrue("owner is one of the values it offers");
        _ownerAccess.IsMatch(" --access shared").ShouldBeFalse();

        _rootedPath.IsMatch("src/Web/packages/api-client/src/generated/").ShouldBeTrue();
        _rootedPath.IsMatch("src/Web/apps/<App>").ShouldBeFalse();
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
            ["one.md"] = "See `src/Present/File.cs`, `./tests/Gone/File.cs`, `Missing.json` and `src/Gone/`.",
            ["two.md"] = "A placeholder `src/<Feature>/File.cs`, prose `src/Gone/File.cs here`, "
                + "a name `Known.json` and a type `Item.Name` name nothing missing.",
        };

        var (missing, checkedPaths) = MissingPaths(
            pages,
            path => path == "src/Present/File.cs",
            name => name == "Known.json");

        checkedPaths.ShouldBe(5);
        missing.ShouldBe(
            [
                "one.md: `tests/Gone/File.cs`",
                "one.md: `Missing.json`, found nowhere in the tree",
                "one.md: `src/Gone/`",
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

    private sealed record Citation(string Page, string Command, string Rest);

    private static HashSet<string> ToolCommands() =>
        CommandsOfTables(File.ReadAllText(Path.Combine(ProjectReferenceGraph.RepositoryRoot, _toolPage)));

    private static HashSet<string> CommandsOfTables(string page) =>
        [.. page.ReplaceLineEndings("\n").Split('\n')
            .Where(line => line.StartsWith("| `", StringComparison.Ordinal))
            .Select(line => line.Split('|')[1])
            .SelectMany(cell => _codeSpan.Matches(cell).Select(span => CommandName(span.Groups["span"].Value)))
            .OfType<string>()];

    private static string? CommandName(string span)
    {
        string spoken = span.StartsWith("dotnet cleanarch ", StringComparison.Ordinal) ? span["dotnet cleanarch ".Length..] : span;
        var words = spoken.Split(' ').TakeWhile(word => word.Length > 0 && char.IsAsciiLetterLower(word[0])).ToList();

        return words.Count == 0 || spoken == span && span.StartsWith("dotnet", StringComparison.Ordinal)
            ? null
            : string.Join(' ', words);
    }

    private static Citation CommandCitation(string text, HashSet<string> commands) =>
        CommandCitations(commands, [("page", text)]).Single();

    private static List<Citation> CommandCitations(HashSet<string> commands) =>
        CommandCitations(
            commands,
            MarkdownPages()
                .Where(page => !_exemptPages.Contains(Path.GetFileName(page), StringComparer.Ordinal))
                .Where(page => !_pagesWithoutCitations.Any(prefix => Relative(page).StartsWith(prefix, StringComparison.Ordinal)))
                .Select(page => (Relative(page), File.ReadAllText(page).Replace("\\\n", " ", StringComparison.Ordinal))));

    private static List<Citation> CommandCitations(HashSet<string> commands, IEnumerable<(string Page, string Text)> pages)
    {
        var groups = commands.Where(command => command.Contains(' ', StringComparison.Ordinal))
            .Select(command => command.Split(' ')[0])
            .ToHashSet(StringComparer.Ordinal);

        return [.. pages.SelectMany(page => _commandCitation.Matches(page.Text).Select(match =>
        {
            string first = match.Groups["first"].Value;
            string second = match.Groups["second"].Value;
            bool grouped = groups.Contains(first) && second.Length > 0;

            return new Citation(
                page.Page,
                grouped ? $"{first} {second}" : first,
                grouped ? match.Groups["rest"].Value : $"{match.Groups["second"].Value} {match.Groups["rest"].Value}".TrimStart());
        }))];
    }

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

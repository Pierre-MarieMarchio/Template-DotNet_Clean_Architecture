using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests;

public sealed class DocsIndexCommandTests : IDisposable
{
    private const string _notice =
        "Written by `dotnet run Tools/Tasks.cs docs-index` from the frontmatter of each page:\nedit the pages, then run it again.";

    private readonly SampleProject _project = new(SampleProject.Minimal);
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    [Fact]
    public void Write_ListsEachFolderByTitle_LeavesOutWhatIsNotIndexed_AndRemovesAnIndexKeptForNoPage()
    {
        Page("README.md", "type: reference\ntitle: Documentation\ndescription: Which page answers what.");
        Page("guides/second.md", "type: guide\ntitle: B task\ndescription: \"How to do B: in one sentence.\"");
        Page("guides/first.md", "type: guide\ntitle: A task\ndescription: How to do A.");
        Page("decisions/README.md", "type: reference\ntitle: Decisions\ndescription: Why each choice was made.");
        Page("decisions/0002-b.md", "type: decision\ntitle: 0002. B\ndescription: Why B.\nstatus: Accepted");
        Page("decisions/0001-a.md", "type: decision\ntitle: 0001. A\ndescription: Why A.\nstatus: Superseded");
        Page("_page-templates/feature.md", "type: template\ntitle: <Feature>\ndescription: <description>");
        Page("plans/plan.md", "title: A plan");
        Directory.CreateDirectory(Path.Combine(_project.Root, "docs", "empty"));
        File.WriteAllText(Path.Combine(_project.Root, "docs", "empty", "index.md"), "# Empty\n");

        Run("docs-index").ShouldBe(CleanArchCommand.Succeeded);

        Read("guides/index.md").ShouldBe(
            "# Guides\n\n" + _notice + "\n\n- [A task](first.md) - How to do A.\n- [B task](second.md) - How to do B: in one sentence.\n");
        Read("decisions/index.md").ShouldBe(
            "# Decisions\n\n" + _notice + "\n\n- [Decisions](README.md) - Why each choice was made.\n"
            + "- [0001. A](0001-a.md) - Superseded. Why A.\n- [0002. B](0002-b.md) - Accepted. Why B.\n");
        Read("index.md").ShouldBe(
            "# Documentation\n\n" + _notice + "\n\n- [Documentation](README.md) - Which page answers what.\n\n## Folders\n\n"
            + "- [Decisions](decisions/index.md) - Why each choice was made.\n- [Guides](guides/index.md)\n");
        File.Exists(DocsPath("plans/index.md")).ShouldBeFalse();
        File.Exists(DocsPath("_page-templates/index.md")).ShouldBeFalse();
        File.Exists(DocsPath("empty/index.md")).ShouldBeFalse();
        _output.ToString().ShouldContain("Wrote 3 index files under docs/.");
    }

    [Fact]
    public void Check_PassesOnWhatWriteProduced_AndRefusesOnceAPageChanges()
    {
        Page("guides/first.md", "type: guide\ntitle: A task\ndescription: How to do A.");
        Run("docs-index").ShouldBe(CleanArchCommand.Succeeded);

        Run("docs-index", "--check").ShouldBe(CleanArchCommand.Succeeded);

        Page("guides/first.md", "type: guide\ntitle: A task\ndescription: How to do A, said otherwise.");
        Run("docs-index", "--check").ShouldBe(CleanArchCommand.Failed);
        _error.ToString().ShouldContain("docs/guides/index.md differs from what it writes");
    }

    [Theory]
    [InlineData("missing", "docs/guides/index.md is missing")]
    [InlineData("orphan", "empty/index.md indexes a folder that holds no page")]
    public void Check_RefusesAnIndexMissingOrKeptForAFolderWithoutPage(string case_, string expected)
    {
        Page("guides/first.md", "type: guide\ntitle: A task\ndescription: How to do A.");
        if (case_ == "orphan")
        {
            Run("docs-index");
            Directory.CreateDirectory(Path.Combine(_project.Root, "docs", "empty"));
            File.WriteAllText(Path.Combine(_project.Root, "docs", "empty", "index.md"), "# Empty\n");
        }

        Run("docs-index", "--check").ShouldBe(CleanArchCommand.Failed);

        _error.ToString().ShouldContain(expected);
    }

    [Theory]
    [InlineData("type: guide\ntitle: C task", "guides/third.md has no description")]
    [InlineData("type: guide\ndescription: Does C.", "guides/third.md has no title")]
    [InlineData("type: decision\ntitle: 0003. C\ndescription: Why C.", "guides/third.md has no status")]
    public void BothModes_RefuseAPageWhoseFrontmatterLacksAField_NamingIt(string frontmatter, string expected)
    {
        Page("guides/third.md", frontmatter);

        Run("docs-index").ShouldBe(CleanArchCommand.Failed);
        _error.ToString().ShouldContain(expected);

        _error.GetStringBuilder().Clear();
        Run("docs-index", "--check").ShouldBe(CleanArchCommand.Failed);
        _error.ToString().ShouldContain(expected);
    }

    [Fact]
    public void Write_OnACopyOfThisRepositorysDocs_ProducesTheIndexesItCommits()
    {
        string source = Path.Combine(Repository.Root, "docs");
        CopyDocs(source, Path.Combine(_project.Root, "docs"));

        Run("docs-index").ShouldBe(CleanArchCommand.Succeeded, _error.ToString());

        string[] committed = [.. Directory.EnumerateFiles(source, "index.md", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(source, file))
            .Where(file => !file.StartsWith("plans", StringComparison.Ordinal))];
        committed.ShouldNotBeEmpty();
        foreach (string index in committed)
        {
            File.ReadAllBytes(DocsPath(index)).ShouldBe(File.ReadAllBytes(Path.Combine(source, index)), index);
        }

        Run("docs-index", "--check").ShouldBe(CleanArchCommand.Succeeded, _error.ToString());
    }

    public void Dispose() => _project.Dispose();

    private static void CopyDocs(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        }

        foreach (string folder in Directory.EnumerateDirectories(source))
        {
            CopyDocs(folder, Path.Combine(target, Path.GetFileName(folder)));
        }
    }

    private string DocsPath(string path) => Path.Combine(_project.Root, "docs", path);

    private string Read(string path) => File.ReadAllText(DocsPath(path));

    private void Page(string path, string frontmatter)
    {
        string full = DocsPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, $"---\n{frontmatter}\n---\n\n# Heading\n");
    }

    private int Run(params string[] arguments) =>
        BuiltInCommands.Run(arguments, _project.Root, _output, _error, new FakeProcesses((_, _, _) => CleanArchCommand.Succeeded));
}

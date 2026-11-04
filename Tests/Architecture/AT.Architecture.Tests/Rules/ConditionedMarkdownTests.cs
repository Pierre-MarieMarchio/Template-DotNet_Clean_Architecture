using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class ConditionedMarkdownTests
{
    private static readonly string[] _skipped = ["bin", "obj", "node_modules", ".git", ".claude", "artifacts", "plans"];

    [Fact]
    public void NoMarkdownPage_KeepsTwoBlankLinesInARow_InAnyFormItIsGeneratedIn()
    {
        var pages = Pages().ToList();

        pages.ShouldNotBeEmpty("No Markdown page with a marker was found, so this rule reads the wrong tree.");

        var symbols = pages
            .SelectMany(page => TemplateMarkers.Conditions(TemplateMarkers.Lines(page.Text)).Values.SelectMany(condition => condition.Names))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var forms = TemplateForms.Load(ProjectReferenceGraph.RepositoryRoot, symbols);
        var offenders = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var (path, text) in pages)
        {
            string[] lines = TemplateMarkers.Lines(text);
            var conditions = TemplateMarkers.Conditions(lines);

            foreach (var form in forms.FormsOf(path, conditions.Values.SelectMany(condition => condition.Names)))
            {
                var kept = TemplateMarkers.Kept(lines, conditions, form.Value).ToList();

                if (HasDoubledBlankLine(kept) || BlockGluedToTheLineBefore(kept) is not null)
                {
                    offenders.Add($"{path} under {form.Options}");
                }
            }
        }

        offenders.ShouldBeEmpty(
            "A generated Markdown page keeps two blank lines in a row, or loses the one before a heading, a table or a code fence, "
            + "because of where the markers sit: move them so every form keeps exactly one blank line, as docs/template/maintaining-the-template.md says under 'Writing docs that condition'.");
    }

    [Fact]
    public void TheRule_FiresOnTwoBlankLinesInARow_AndStaysSilentOnOne()
    {
        HasDoubledBlankLine(["text", "", "", "## Next"]).ShouldBeTrue();
        HasDoubledBlankLine(["text", "", "## Next"]).ShouldBeFalse();
    }

    [Fact]
    public void TheRule_FiresOnAHeadingATableOrAFenceGluedToTheLineBefore_AndStaysSilentOnOneAfterABlankLine()
    {
        BlockGluedToTheLineBefore(["text", "## Next"]).ShouldBe("## Next");
        BlockGluedToTheLineBefore(["text", "| a | b |"]).ShouldBe("| a | b |");
        BlockGluedToTheLineBefore(["text", "```bash"]).ShouldBe("```bash");
        BlockGluedToTheLineBefore(["text", "", "## Next", "", "| a |", "|---|", "", "```bash", "# not a heading", "```", "", "text"]).ShouldBeNull();
        BlockGluedToTheLineBefore(["---", "title: x", "---", "## Next"]).ShouldBeNull();
        BlockGluedToTheLineBefore(["## Next", "text"]).ShouldBeNull();
    }

    private static string? BlockGluedToTheLineBefore(List<string> lines)
    {
        int first = lines.Count > 0 && lines[0] == "---" ? lines.IndexOf("---", 1) + 1 : 0;
        bool fenced = false;

        for (int index = Math.Max(first, 0); index < lines.Count; index++)
        {
            string line = lines[index].TrimStart();
            bool fence = line.StartsWith("```", StringComparison.Ordinal);
            bool startsBlock = !fenced
                && (fence
                    || (line.StartsWith('#') && line.TrimStart('#').StartsWith(' '))
                    || (line.StartsWith('|') && (index == first || !lines[index - 1].TrimStart().StartsWith('|'))));

            if (startsBlock && index > first && lines[index - 1].Trim().Length > 0)
            {
                return lines[index];
            }

            fenced ^= fence;
        }

        return null;
    }

    private static bool HasDoubledBlankLine(List<string> lines) =>
        lines.Zip(lines.Skip(1)).Any(pair => pair.First.Trim().Length == 0 && pair.Second.Trim().Length == 0);

    private static IEnumerable<(string Path, string Text)> Pages()
    {
        string root = ProjectReferenceGraph.RepositoryRoot;

        return Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories)
            .Select(file => (Path: Path.GetRelativePath(root, file).Replace('\\', '/'), File: file))
            .Where(page => !page.Path.Split('/').Any(part => _skipped.Contains(part, StringComparer.Ordinal)))
            .Select(page => (page.Path, Text: File.ReadAllText(page.File)))
            .Where(page => page.Text.Contains("<!--#", StringComparison.Ordinal));
    }
}

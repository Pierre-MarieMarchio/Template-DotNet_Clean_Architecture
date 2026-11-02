using System.Xml.Linq;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class CodeStyleEnforcementTests
{
    private static readonly string _root = ProjectReferenceGraph.RepositoryRoot;

    [Theory]
    [InlineData("EnforceCodeStyleInBuild")]
    [InlineData("TreatWarningsAsErrors")]
    [InlineData("GenerateDocumentationFile")]
    public void TheBuild_RunsTheCodeStyleAnalysis_AndFailsOnIt(string property)
    {
        string[] values = [.. XDocument
            .Load(Path.Combine(_root, "Directory.Build.props"))
            .Descendants(property)
            .Select(element => element.Value.Trim())];

        values.ShouldBe(
            ["true"],
            $"Directory.Build.props sets {property} once, to true: without it a style rule the build "
            + "should hold goes unseen until a separate dotnet format run, which verify no longer makes. "
            + "GenerateDocumentationFile is what lets the build report IDE0005.");
    }

    [Theory]
    [InlineData("IDE0005")]
    [InlineData("IDE0055")]
    [InlineData("IDE1006")]
    public void TheBuild_ReportsTheRule_ThatOnlyARuleSeverityEnables(string rule)
    {
        string[] severities = [.. CSharpSection()
            .Select(line => line.Split('=', 2))
            .Where(pair => pair.Length == 2 && pair[0].Trim() == $"dotnet_diagnostic.{rule}.severity")
            .Select(pair => pair[1].Split('#')[0].Trim())];

        severities.ShouldHaveSingleItem(
            $".editorconfig's [*.cs] section sets dotnet_diagnostic.{rule}.severity once: the rule has "
            + "no option whose severity the build reads, so without this line dotnet format alone "
            + "reports it.");
        severities[0].ShouldBeOneOf(["warning", "error"]);
    }

    private static IEnumerable<string> CSharpSection() =>
        File.ReadLines(Path.Combine(_root, ".editorconfig"))
            .SkipWhile(line => line.Trim() != "[*.cs]")
            .Skip(1)
            .TakeWhile(line => !line.TrimStart().StartsWith('['));
}

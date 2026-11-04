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
        string[] values = PropertyValues(XDocument.Load(Path.Combine(_root, "Directory.Build.props")), property);

        values.ShouldBe(
            ["true"],
            $"Directory.Build.props sets {property} once, to true: without it a style rule the build "
            + "should hold goes unseen until a separate dotnet format run, which verify no longer makes. "
            + "GenerateDocumentationFile is what lets the build report IDE0005. Set the property "
            + "once, to true, in Directory.Build.props.");
    }

    [Fact]
    public void TheBuild_FailsOnAWarningAnMSBuildTaskLogs()
    {
        string[] values = PropertyValues(
            XDocument.Load(Path.Combine(_root, "Directory.Build.props")),
            "MSBuildTreatWarningsAsErrors");

        values.ShouldBe(
            ["true"],
            "Directory.Build.props sets MSBuildTreatWarningsAsErrors once, to true: TreatWarningsAsErrors "
            + "reaches the compilers alone, so a warning a build task logs, such as Avalonia's XAML "
            + "compiler reporting an obsolete member, passes the build until someone builds with "
            + "-warnaserror. Set the property once, to true, in Directory.Build.props.");
    }

    [Theory]
    [InlineData("IDE0005")]
    [InlineData("IDE0055")]
    [InlineData("IDE1006")]
    public void TheBuild_ReportsTheRule_ThatOnlyARuleSeverityEnables(string rule)
    {
        string[] severities = SeveritiesOf(CSharpSection(), rule);

        severities.ShouldHaveSingleItem(
            $".editorconfig's [*.cs] section sets dotnet_diagnostic.{rule}.severity once: the rule has "
            + "no option whose severity the build reads, so without this line dotnet format alone "
            + "reports it. Add the line to the [*.cs] section of .editorconfig with the severity "
            + "'warning'.");
        severities[0].ShouldBeOneOf(["warning", "error"]);
    }

    [Fact]
    public void TheReaders_AreSensitive_AndSeeADuplicateAMissingAndAFalseSetting()
    {
        var props = XDocument.Parse(
            "<Project><PropertyGroup><A>true</A><B> false </B></PropertyGroup><PropertyGroup><A>true</A></PropertyGroup></Project>");

        PropertyValues(props, "A").ShouldBe(["true", "true"]);
        PropertyValues(props, "B").ShouldBe(["false"]);
        PropertyValues(props, "C").ShouldBeEmpty();

        string[] editorconfig =
        [
            "root = true",
            "[*.cs]",
            "dotnet_diagnostic.IDE0005.severity = warning # kept",
            "dotnet_diagnostic.IDE0055.severity=error",
            "[*.md]",
            "dotnet_diagnostic.IDE1006.severity = warning",
        ];

        SeveritiesOf(SectionOf(editorconfig, "[*.cs]"), "IDE0005").ShouldBe(["warning"]);
        SeveritiesOf(SectionOf(editorconfig, "[*.cs]"), "IDE0055").ShouldBe(["error"]);
        SeveritiesOf(SectionOf(editorconfig, "[*.cs]"), "IDE1006").ShouldBeEmpty();
    }

    private static string[] PropertyValues(XDocument document, string property) =>
        [.. document.Descendants(property).Select(element => element.Value.Trim())];

    private static string[] SeveritiesOf(IEnumerable<string> section, string rule) =>
        [.. section
            .Select(line => line.Split('=', 2))
            .Where(pair => pair.Length == 2 && pair[0].Trim() == $"dotnet_diagnostic.{rule}.severity")
            .Select(pair => pair[1].Split('#')[0].Trim())];

    private static IEnumerable<string> CSharpSection() =>
        SectionOf(File.ReadLines(Path.Combine(_root, ".editorconfig")), "[*.cs]");

    private static IEnumerable<string> SectionOf(IEnumerable<string> lines, string header) =>
        lines
            .SkipWhile(line => line.Trim() != header)
            .Skip(1)
            .TakeWhile(line => !line.TrimStart().StartsWith('['));
}

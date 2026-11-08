using System.Text.RegularExpressions;
using System.Xml.Linq;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed partial class NuGetAuditPolicyTests
{
    private static readonly string _root = ProjectReferenceGraph.RepositoryRoot;

    private static readonly string[] _scratchFolders = ["bin", "obj", "node_modules", "artifacts", ".git", ".claude"];

    [Theory]
    [InlineData("NuGetAudit", "true")]
    [InlineData("NuGetAuditMode", "all")]
    [InlineData("NuGetAuditLevel", "low")]
    public void TheBuild_AuditsEveryDependencyAtTheLowestLevel(string property, string expected)
    {
        string[] values = PropertyValues(XDocument.Load(Path.Combine(_root, "Directory.Build.props")), property);

        values.ShouldBe(
            [expected],
            $"Directory.Build.props sets {property} once, to {expected}: any advisory on any dependency "
            + "fails the build, as docs/guides/build-and-ci.md says under An advisory fails the build. Restore the value, and raise the dependency or "
            + "suppress the one advisory with NuGetAuditSuppress instead of relaxing the policy.");
    }

    [Fact]
    public void NoBuildFile_SilencesTheAuditWarnings()
    {
        string[] offenders =
        [
            .. BuildFiles()
                .SelectMany(file => SilencedAuditCodes(XDocument.Load(file))
                    .Select(code => $"{Path.GetRelativePath(_root, file)}: {code}")),
        ];

        offenders.ShouldBeEmpty(
            "A NoWarn or WarningsNotAsErrors naming NU1901 to NU1904 hides an advisory from the build, "
            + "against the rule that any known advisory fails it (docs/guides/build-and-ci.md). Remove it, raise the dependency, or list the advisory in "
            + "NuGetAuditSuppress with its reason.");
    }

    [Fact]
    public void TheReaders_AreSensitive_ToADuplicateAMissingValueAndASilencedCode()
    {
        var props = XDocument.Parse(
            "<Project><PropertyGroup><NuGetAuditLevel> low </NuGetAuditLevel></PropertyGroup>"
            + "<PropertyGroup><NuGetAuditLevel>high</NuGetAuditLevel></PropertyGroup></Project>");

        PropertyValues(props, "NuGetAuditLevel").ShouldBe(["low", "high"]);
        PropertyValues(props, "NuGetAuditMode").ShouldBeEmpty();

        var silenced = XDocument.Parse(
            "<Project><PropertyGroup><NoWarn>$(NoWarn);CS1591;NU1903</NoWarn>"
            + "<WarningsNotAsErrors>NU1901;NU1701</WarningsNotAsErrors>"
            + "<Other>NU1904</Other></PropertyGroup></Project>");

        SilencedAuditCodes(silenced).ShouldBe(["NU1903", "NU1901"]);
        SilencedAuditCodes(XDocument.Parse("<Project><PropertyGroup><NoWarn>CS1591;NU1701</NoWarn></PropertyGroup></Project>"))
            .ShouldBeEmpty();
    }

    private static string[] PropertyValues(XDocument document, string property) =>
        [.. document.Descendants(property).Select(element => element.Value.Trim())];

    private static string[] SilencedAuditCodes(XDocument document) =>
        [.. document.Descendants()
            .Where(element => element.Name.LocalName is "NoWarn" or "WarningsNotAsErrors")
            .SelectMany(element => AuditCode().Matches(element.Value).Select(match => match.Value))];

    private static IEnumerable<string> BuildFiles() =>
        RepositoryTree.SourceFiles(_root, "*.*", _scratchFolders)
            .Where(path => Path.GetExtension(path) is ".csproj" or ".props" or ".targets");

    [GeneratedRegex(@"NU190[1-4]")]
    private static partial Regex AuditCode();
}

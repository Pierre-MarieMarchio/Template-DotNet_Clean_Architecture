using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class ExternalKeyTests
{
    private static readonly Dictionary<string, string> _sectionExceptions = new(StringComparer.Ordinal)
    {
        //#if (localAuth)
        ["IdentityPolicyOptions"] = "Identity",
        ["IdentityTokenOptions"] = "IdentityTokens",
        //#endif
        ["ProblemTypeOptions"] = "ProblemTypes",
        ["SecurityHeaderOptions"] = "SecurityHeaders",
        ["TelemetryOptions"] = "OpenTelemetry",
    };

    private static readonly Regex _sectionName = new(
        @"public\s+const\s+string\s+SectionName\s*=\s*""([^""]+)""",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    //#if (localAuth)
    private static readonly Regex _providerName = new(
        @"public\s+const\s+string\s+Value\s*=\s*""([^""]+)""",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));
    //#endif

    [Fact]
    public void EverySectionName_IsItsOptionsTypeName()
    {
        var declared = ConstantsIn("*Options.cs", _sectionName);

        declared.Count.ShouldBeGreaterThanOrEqualTo(
            18,
            "Far fewer options classes were found than this template holds, so this rule is reading " +
            "the wrong tree and passing for the wrong reason.");

        var offenders = declared
            .Where(entry => Expected(entry.Key) != entry.Value.Replace(":", "", StringComparison.Ordinal))
            .Select(entry => $"{entry.Key}.SectionName is \"{entry.Value}\", expected " +
                $"\"{Expected(entry.Key)}\"")
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.ShouldBeEmpty(
            "A configuration section name is a key in an operator's file. Anchoring it to the type " +
            "name is what makes a rename of the type fail here instead of at the next deployment.");
    }
    //#if (localAuth)

    [Fact]
    public void EveryTokenProviderName_IsItsTypeNameWithoutTheSuffix()
    {
        var declared = ConstantsIn("*TokenProviderName.cs", _providerName);

        declared.Count.ShouldBeGreaterThanOrEqualTo(
            2,
            "No token-provider name constant was found, so this rule is checking an empty set.");

        var offenders = declared
            .Where(entry => entry.Key.Replace("TokenProviderName", "", StringComparison.Ordinal) != entry.Value)
            .Select(entry => $"{entry.Key}.Value is \"{entry.Value}\"")
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.ShouldBeEmpty(
            "A provider name is written into AspNetUserTokens. A value that no longer matches its " +
            "type name is either a rename nobody meant, or one that needs a migration.");
    }
    //#endif

    private static string Expected(string typeName) =>
        _sectionExceptions.TryGetValue(typeName, out string? section)
            ? section
            : typeName.Replace("Options", "", StringComparison.Ordinal);

    private static Dictionary<string, string> ConstantsIn(string filePattern, Regex constant)
    {
        string source = Path.Combine(ProjectReferenceGraph.RepositoryRoot, "Src");

        var found = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string file in Directory.EnumerateFiles(source, filePattern, SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var match = constant.Match(File.ReadAllText(file));

            if (match.Success)
            {
                found[Path.GetFileNameWithoutExtension(file)] = match.Groups[1].Value;
            }
        }

        return found;
    }
}

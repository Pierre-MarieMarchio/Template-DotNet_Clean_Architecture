using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

/// <summary>
/// Constants whose value is read by something outside this repository: a configuration section an
/// operator writes in <c>appsettings.json</c>, and a token-provider name ASP.NET Identity stores
/// against a row. Renaming one of these is not a refactor — it is a breaking change to a contract
/// the compiler cannot see, and every test in this repository can stay green while it happens.
/// <para>
/// That is not hypothetical. A rename of a type called <c>PasswordReset</c> swept
/// <c>PasswordResetOptions.SectionName</c>, the four <c>appsettings.json</c> files carrying that
/// section, and the composition test's own configuration dictionary, all at once. Everything agreed
/// with everything else, all 2 135 tests passed, and the only thing that had changed was the key an
/// operator's existing configuration file uses. These rules anchor the values to the type name
/// instead, so a sweep that moves both still fails.
/// </para>
/// </summary>
public sealed class ExternalKeyTests
{
    /// <summary>
    /// Options whose section is deliberately not their type name. Each is an existing external
    /// contract that predates the convention, so the exception is written down rather than the
    /// convention weakened — and the list is what a reviewer reads when someone proposes a sixth.
    /// </summary>
    private static readonly Dictionary<string, string> _sectionExceptions = new(StringComparer.Ordinal)
    {
        ["IdentityPolicyOptions"] = "Identity",
        ["IdentityTokenOptions"] = "IdentityTokens",
        ["ProblemTypeOptions"] = "ProblemTypes",
        ["SecurityHeaderOptions"] = "SecurityHeaders",
        ["TelemetryOptions"] = "OpenTelemetry",
        ["WorkerTelemetryOptions"] = "OpenTelemetry",
    };

    private static readonly Regex _sectionName = new(
        @"public\s+const\s+string\s+SectionName\s*=\s*""([^""]+)""",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex _providerName = new(
        @"public\s+const\s+string\s+Value\s*=\s*""([^""]+)""",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    /// <summary>
    /// An options class named <c>XOptions</c> binds the section named <c>X</c>, unless it is one of
    /// the six that say otherwise above.
    /// </summary>
    [Fact]
    public void EverySectionName_IsItsOptionsTypeName()
    {
        var declared = ConstantsIn("*Options.cs", _sectionName);

        declared.Count.ShouldBeGreaterThanOrEqualTo(
            20,
            "Far fewer options classes were found than this template holds, so this rule is reading " +
            "the wrong tree and passing for the wrong reason.");

        var offenders = declared
            .Where(entry => Expected(entry.Key) != entry.Value)
            .Select(entry => $"{entry.Key}.SectionName is \"{entry.Value}\", expected " +
                $"\"{Expected(entry.Key)}\"")
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.ShouldBeEmpty(
            "A configuration section name is a key in an operator's file. Anchoring it to the type " +
            "name is what makes a rename of the type fail here instead of at the next deployment.");
    }

    /// <summary>
    /// A token-provider name is handed to ASP.NET Identity and stored against the user row that
    /// holds the token. Changing it does not fail to compile and does not fail a test; it silently
    /// invalidates every token already issued under the old name.
    /// </summary>
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

using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class ErrorCodeTableTests
{
    private const string _codeShape = @"[a-z][A-Za-z]*(?:\.[a-z][A-Za-z]*)+";

    private static readonly Regex _quotedCode = new($"\"({_codeShape})\"", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static readonly Regex _citedCode = new($"`({_codeShape})`", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static readonly string[] _foldersThatAnswerNoRequest = ["Jobs", "Locks"];

    [Fact]
    public void EveryCodeTheSourceDeclares_HasARowInTheApiReference()
    {
        var cited = CitedCodes();

        string[] offenders =
        [
            .. DeclaredCodes()
                .Where(declared => !cited.Contains(declared.Key))
                .Select(declared => $"{declared.Key} ({string.Join(", ", declared.Value)})"),
        ];

        offenders.ShouldBeEmpty(
            "A client branches on a problem's code, so every code the API can answer has its row in " +
            "docs/reference/api.md. A string shaped like one in a folder that answers no request " +
            $"({string.Join(", ", _foldersThatAnswerNoRequest)}) is excused; these are not.");
    }

    [Fact]
    public void EveryCodeTheApiReferenceLists_IsDeclaredInTheSource()
    {
        var declared = DeclaredCodes();

        string[] offenders = [.. CitedCodes().Where(code => !declared.ContainsKey(code)).Order(StringComparer.Ordinal)];

        offenders.ShouldBeEmpty(
            "docs/reference/api.md lists a code no source declares, so a client is told to branch on a " +
            "refusal the API never sends.");
    }

    [Theory]
    [InlineData("Error.NotFound(\"todoList.notFound\", message)", "todoList.notFound")]
    [InlineData("internal const string UnauthorizedCode = \"auth.required\";", "auth.required")]
    [InlineData("problem.Extensions[\"code\"] = \"request.timeout\";", "request.timeout")]
    public void TheShape_FindsACodeWhateverDeclaresIt(string source, string code) =>
        _quotedCode.Matches(source).Select(match => match.Groups[1].Value).ShouldBe([code]);

    [Theory]
    [InlineData("\"application/problem+json\"")]
    [InlineData("\"Jwt:Key\"")]
    [InlineData("\"AppTemplate.Api\"")]
    [InlineData("\"notFound\"")]
    public void TheShape_LeavesOtherStringsAlone(string source) =>
        _quotedCode.IsMatch(source).ShouldBeFalse();

    private static HashSet<string> CitedCodes()
    {
        string reference = File.ReadAllText(
            Path.Combine(ProjectReferenceGraph.RepositoryRoot, "docs", "reference", "api.md"));

        int section = reference.IndexOf("## Error codes", StringComparison.Ordinal);

        section.ShouldBeGreaterThanOrEqualTo(0, "docs/reference/api.md has no '## Error codes' section to read.");

        HashSet<string> cited =
        [
            .. reference[section..]
                .Split('\n')
                .Where(line => line.StartsWith('|'))
                .SelectMany(line => _citedCode.Matches(line.Split('|')[1]))
                .Select(match => match.Groups[1].Value),
        ];

        cited.ShouldNotBeEmpty("The error-code tables of docs/reference/api.md read as empty, so neither direction is checked.");

        return cited;
    }

    private static Dictionary<string, SortedSet<string>> DeclaredCodes()
    {
        string source = Path.Combine(ProjectReferenceGraph.RepositoryRoot, "Src");
        var declared = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        foreach (string file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
        {
            string[] folders = Path.GetRelativePath(source, file).Split(Path.DirectorySeparatorChar);

            if (folders.Any(folder => folder is "bin" or "obj" or "Migrations" || _foldersThatAnswerNoRequest.Contains(folder)))
            {
                continue;
            }

            foreach (Match match in _quotedCode.Matches(File.ReadAllText(file)))
            {
                string code = match.Groups[1].Value;

                if (!declared.TryGetValue(code, out var files))
                {
                    files = new SortedSet<string>(StringComparer.Ordinal);
                    declared[code] = files;
                }

                files.Add(Path.GetFileName(file));
            }
        }

        declared.ShouldNotBeEmpty("No code-shaped string was found under Src, so the walk is not reading the source it describes.");

        return declared;
    }
}

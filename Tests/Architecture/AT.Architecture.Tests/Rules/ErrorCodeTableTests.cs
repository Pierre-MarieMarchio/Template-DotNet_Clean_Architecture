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

    private static readonly string[] _foldersThatAnswerNoRequest = ["Clients", "Jobs", "Locks"];

    [Fact]
    public void EveryCodeTheSourceDeclares_HasARowInTheApiReference()
    {
        string[] offenders = [.. Undocumented(DeclaredCodes(), CitedCodes())];

        offenders.ShouldBeEmpty(
            "A client branches on a problem's code, so every code the API can answer has its row in " +
            "docs/reference/api.md. A string shaped like one in a folder that answers no request " +
            $"({string.Join(", ", _foldersThatAnswerNoRequest)}) is excused; these are not. Add each " +
            "line shown here to the table of the section under '## Error codes' that fits it, with " +
            "its status and when it is answered.");
    }

    [Fact]
    public void EveryCodeTheApiReferenceLists_IsDeclaredInTheSource()
    {
        string[] offenders = [.. NotDeclared(CitedCodes(), DeclaredCodes())];

        offenders.ShouldBeEmpty(
            "docs/reference/api.md lists a code no source declares, so a client is told to branch on a " +
            "refusal the API never sends. Delete the row of each code named here from the table of " +
            "docs/reference/api.md, or declare the code in the source if it is still answered.");
    }

    [Fact]
    public void TheComparison_IsSensitive_AndNamesTheRowToAddAndTheRowToDelete()
    {
        var declared = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal)
        {
            ["todoList.notFound"] = new(["TodoListErrors.cs"], StringComparer.Ordinal),
            ["todoList.archived"] = new(["TodoListErrors.cs", "Guard.cs"], StringComparer.Ordinal),
        };
        HashSet<string> cited = ["todoList.notFound", "todoList.retired"];

        Undocumented(declared, cited).ShouldBe(
            ["todoList.archived (Guard.cs, TodoListErrors.cs): add the line | `todoList.archived` | <status> | <when it is answered> |"]);

        NotDeclared(cited, declared).ShouldBe(["todoList.retired"]);

        Undocumented(declared, new HashSet<string> { "todoList.notFound", "todoList.archived" }).ShouldBeEmpty();
        NotDeclared(["todoList.notFound"], declared).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Src/Infrastructure/Jobs/PurgeJob.cs", true)]
    [InlineData("Src/Application/Locks/Lease.cs", true)]
    [InlineData("Src/Infrastructure/Persistence/Migrations/Init.cs", true)]
    [InlineData("Src/Application/Features/Todo/Errors.cs", false)]
    public void TheExcusal_IsSensitive_AndSkipsOnlyWhatAnswersNoRequest(string path, bool excused) =>
        IsExcused(path.Split('/')).ShouldBe(excused);

    private static List<string> Undocumented(
        Dictionary<string, SortedSet<string>> declared,
        HashSet<string> cited) =>
        [.. declared
            .Where(code => !cited.Contains(code.Key))
            .Select(code => $"{code.Key} ({string.Join(", ", code.Value)}): add the line | `{code.Key}` | <status> | <when it is answered> |")
            .Order(StringComparer.Ordinal)];

    private static List<string> NotDeclared(
        IEnumerable<string> cited,
        Dictionary<string, SortedSet<string>> declared) =>
        [.. cited.Where(code => !declared.ContainsKey(code)).Order(StringComparer.Ordinal)];

    private static bool IsExcused(string[] folders) =>
        folders.Any(folder => folder is "bin" or "obj" or "Migrations" || _foldersThatAnswerNoRequest.Contains(folder));

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

            if (IsExcused(folders))
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

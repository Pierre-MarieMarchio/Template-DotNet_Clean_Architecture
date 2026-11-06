using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CleanArch.Cli.Commands.Kernel;

internal static partial class SolutionFilter
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Write(string root, string solution, string fileName, IEnumerable<string> projects)
    {
        string directory = Path.Combine(root, "artifacts", "test");
        Directory.CreateDirectory(directory);
        string filter = Path.Combine(directory, fileName);
        string solutionDirectory = Path.GetDirectoryName(solution) ?? root;

        var content = new JsonObject
        {
            ["solution"] = new JsonObject
            {
                ["path"] = ProjectPath.Relative(directory, solution),
                ["projects"] = new JsonArray([.. projects
                    .Select(project => JsonValue.Create(ProjectPath.Relative(solutionDirectory, project)))]),
            },
        };
        File.WriteAllText(filter, content.ToJsonString(_json));
        return filter;
    }

    public static string WriteRunnable(string root, string solution, string[] skipped)
    {
        HashSet<string> left = [.. skipped.Select(Path.GetFullPath)];

        return Write(
            root,
            solution,
            "runnable.slnf",
            ProjectPaths(solution)
                .Select(path => Path.GetFullPath(Path.Combine(root, path)))
                .Where(path => !left.Contains(path)));
    }

    public static string WriteDockerFree(string root, string solution, string[] tests, string[] skipped) =>
        Write(root, solution, "docker-free.slnf", tests.Where(TestProjects.IsDockerFree).Except(skipped, StringComparer.Ordinal));

    public static void Delete(string? filter)
    {
        if (filter is not null)
        {
            File.Delete(filter);
        }
    }

    public static string[] ProjectPaths(string solution)
    {
        string text = File.ReadAllText(solution);
        IEnumerable<string> paths = solution.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
            ? XDocument.Parse(text).Descendants("Project").Select(element => element.Attribute("Path")?.Value).OfType<string>()
            : ProjectReference().Matches(text).Select(match => match.Groups["path"].Value);
        return [.. paths.Select(ProjectPath.Shown).Order(StringComparer.Ordinal)];
    }

    [GeneratedRegex(@"""(?<path>[^""]+\.csproj)""", RegexOptions.None, matchTimeoutMilliseconds: 5000)]
    private static partial Regex ProjectReference();
}

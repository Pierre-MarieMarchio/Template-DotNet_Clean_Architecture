using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CleanArch.Cli;

internal static class SolutionFilter
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
                ["path"] = Path.GetRelativePath(directory, solution).Replace('\\', '/'),
                ["projects"] = new JsonArray([.. projects
                    .Select(project => JsonValue.Create(Path.GetRelativePath(solutionDirectory, project).Replace('\\', '/')))]),
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
            SolutionLocator.ProjectPaths(root, solution)
                .Select(path => Path.GetFullPath(Path.Combine(root, path)))
                .Where(path => !left.Contains(path)));
    }

    public static void Delete(string? filter)
    {
        if (filter is not null)
        {
            File.Delete(filter);
        }
    }
}

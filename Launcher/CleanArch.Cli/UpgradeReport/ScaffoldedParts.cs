using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CleanArch.Cli.UpgradeReport;

internal sealed class ScaffoldedParts
{
    public const string Descriptor = "scaffold.json";

    private const string _webScaffold = "web-scaffold";

    private static readonly Regex _descriptorPath = new(@"^Src/(?:Presentation|Web/apps)/[^/]+/scaffold\.json$", RegexOptions.None, TimeSpan.FromSeconds(2));

    private readonly Dictionary<string, string> _tasks;

    private ScaffoldedParts(Dictionary<string, string> tasks, bool addToTheSolution)
    {
        _tasks = tasks;
        AddToTheSolution = addToTheSolution;
    }

    public bool AddToTheSolution { get; }

    public static ScaffoldedParts In(string older, string newer, IEnumerable<string> generatedPaths)
    {
        Dictionary<string, string> tasks = generatedPaths
            .Where(path => Path.GetFileName(path) == Descriptor && _descriptorPath.IsMatch(path))
            .Select(path => path[..^(Descriptor.Length + 1)])
            .ToDictionary(folder => folder, folder => folder.StartsWith("Src/Web/", StringComparison.Ordinal) ? _webScaffold : "presentation-scaffold", StringComparer.Ordinal);
        bool addToTheSolution = tasks.Keys.Any(folder => new[] { older, newer }
            .Select(tree => Path.Combine(tree, folder, Descriptor))
            .Where(File.Exists)
            .Any(descriptor => JsonNode.Parse(File.ReadAllText(descriptor))?["kind"]?.GetValue<string>() is "dotnet" or "written"));

        return new ScaffoldedParts(tasks, addToTheSolution);
    }

    public string? FolderOf(string path) => _tasks.Keys.FirstOrDefault(folder => path.StartsWith(folder + "/", StringComparison.Ordinal));

    public string TaskOf(string folder) => _tasks[folder];

    public string CommandOf(string folder, string name)
    {
        string task = _tasks[folder];
        string part = Path.GetFileName(folder);
        string argument = task == _webScaffold ? part : name + "." + part[(part.IndexOf('.', StringComparison.Ordinal) + 1)..];

        return $"dotnet run Tools/Tasks.cs {task} {argument}";
    }
}

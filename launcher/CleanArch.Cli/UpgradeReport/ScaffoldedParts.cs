using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CleanArch.Cli.UpgradeReport;

internal sealed class ScaffoldedParts
{
    public const string Descriptor = "scaffold.json";

    private static readonly Regex _descriptorPath = new(@"^src/(?:Presentation|Web/apps)/[^/]+/scaffold\.json$", RegexOptions.None, TimeSpan.FromSeconds(2));

    private readonly Dictionary<string, string> _commands;

    private ScaffoldedParts(Dictionary<string, string> commands, bool addToTheSolution)
    {
        _commands = commands;
        AddToTheSolution = addToTheSolution;
    }

    public bool AddToTheSolution { get; }

    public static ScaffoldedParts In(string older, string newer, IEnumerable<string> generatedPaths)
    {
        Dictionary<string, string> commands = generatedPaths
            .Where(path => Path.GetFileName(path) == Descriptor && _descriptorPath.IsMatch(path))
            .Select(path => path[..^(Descriptor.Length + 1)])
            .ToDictionary(folder => folder, CompletedBy, StringComparer.Ordinal);
        bool addToTheSolution = commands.Keys.Any(folder => new[] { older, newer }
            .Select(tree => Path.Combine(tree, folder, Descriptor))
            .Where(File.Exists)
            .Any(descriptor => JsonNode.Parse(File.ReadAllText(descriptor))?["kind"]?.GetValue<string>() is "dotnet" or "written"));

        return new ScaffoldedParts(commands, addToTheSolution);
    }

    public string? FolderOf(string path) => _commands.Keys.FirstOrDefault(folder => path.StartsWith(folder + "/", StringComparison.Ordinal));

    public string CommandOf(string folder) => _commands[folder];

    private static string CompletedBy(string folder)
    {
        string part = Path.GetFileName(folder);
        return folder.StartsWith("src/Web/", StringComparison.Ordinal)
            ? $"{Tool.Name} add web-app --name {part}"
            : $"{Tool.Name} add presentation --name {part[(part.IndexOf('.', StringComparison.Ordinal) + 1)..]}";
    }
}

using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CleanArch.Cli;

internal static class SolutionLocator
{
    public static string[] ProjectPaths(string root, string solution)
    {
        string text = File.ReadAllText(solution);
        IEnumerable<string> paths = solution.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
            ? XDocument.Parse(text).Descendants("Project").Select(element => element.Attribute("Path")?.Value).OfType<string>()
            : Regex.Matches(text, @"""(?<path>[^""]+\.csproj)""").Select(match => match.Groups["path"].Value);
        return [.. paths.Select(path => path.Replace('\\', '/')).Order(StringComparer.Ordinal)];
    }

    public static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
}

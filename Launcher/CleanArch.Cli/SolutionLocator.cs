using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CleanArch.Cli;

internal static class SolutionLocator
{
    public static string Find(LocatedProject project)
    {
        string byName = Path.Combine(project.Root, project.File.Name + ".sln");
        if (File.Exists(byName))
        {
            return byName;
        }

        string byNameXml = Path.Combine(project.Root, project.File.Name + ".slnx");
        if (File.Exists(byNameXml))
        {
            return byNameXml;
        }

        string[] any = [.. Directory.EnumerateFiles(project.Root, "*.sln")
            .Concat(Directory.EnumerateFiles(project.Root, "*.slnx"))
            .Order(StringComparer.Ordinal)];
        return any.Length > 0 ? any[0] : byName;
    }

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

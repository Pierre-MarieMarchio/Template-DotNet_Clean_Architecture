using CleanArch.Tooling.Projects;

namespace CleanArch.Cli.Generators.Shared;

internal static class ProjectFolders
{
    internal const string Src = "Src";

    internal const string Presentation = "Src/Presentation";

    internal static string FolderOf(string product, string part) => ProjectFacts.FolderOf(product, part);

    internal static string Initials(string product) => FolderOf(product, string.Empty)[..^1];

    internal static string SourceProject(string product, string layer) => $"{Src}/{layer}/{FolderOf(product, layer)}";

    internal static string Full(string root, string relative) => Path.Combine([root, .. relative.Split('/')]);
}

using System.Text.Json.Nodes;

namespace AppTemplate.Architecture.Tests.Fixtures;

internal static class ProjectFolders
{
    internal static string ProductName { get; } = ReadProductName();

    internal static string FolderOf(string project) =>
        project.StartsWith(ProductName + ".", StringComparison.Ordinal)
            ? InitialsOf(ProductName) + project[ProductName.Length..]
            : throw new InvalidOperationException(
                $"'{project}' does not start with the product's name, {ProductName}, as every project's name does.");

    internal static string InitialsOf(string product) => string.Concat(product.Split('.').Select(SegmentInitials));

    private static string SegmentInitials(string segment) =>
        segment.Any(char.IsAsciiLetterUpper)
            ? string.Concat(segment.Where(char.IsAsciiLetterUpper))
            : string.Concat(segment.Where(char.IsAsciiLetter).Take(1).Select(char.ToUpperInvariant));

    private static string ReadProductName()
    {
        string path = Path.Combine(ProjectReferenceGraph.RepositoryRoot, "cleanarch.json");

        return JsonNode.Parse(File.ReadAllText(path))?["name"]?.GetValue<string>() is { Length: > 0 } name
            ? name
            : throw new InvalidOperationException($"{path} records no name, which every project's name starts with.");
    }
}

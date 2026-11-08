using System.Text.Json;

namespace AppTemplate.AppHost.Common.Hosting;

internal sealed record ProjectFile(
    string Root,
    string Name,
    IReadOnlyList<PresentationProject> Presentation,
    IReadOnlyList<WebApp> WebApps)
{
    internal const string FileName = "cleanarch.json";

    internal static ProjectFile Above(string directory)
    {
        for (DirectoryInfo? current = new(directory); current is not null; current = current.Parent)
        {
            string path = Path.Combine(current.FullName, FileName);

            if (File.Exists(path))
            {
                return Read(current.FullName, File.ReadAllText(path));
            }
        }

        throw new InvalidOperationException(
            $"{FileName} is in neither '{directory}' nor any directory above it, so the app host cannot tell "
            + "which hosts the project holds. Run the app host from the project it belongs to.");
    }

    internal static ProjectFile Read(string root, string json)
    {
        using var document = JsonDocument.Parse(json);
        var file = document.RootElement;

        return new ProjectFile(
            root,
            file.GetProperty("name").GetString() ?? throw new InvalidOperationException($"{FileName} records no name."),
            [.. Entries(file, "presentation").Select(entry => new PresentationProject(Text(entry, "name"), Text(entry, "framework")))],
            file.TryGetProperty("web", out var web)
                ? [.. Entries(web, "apps").Select(entry => new WebApp(Text(entry, "name"), Text(entry, "framework"), Text(entry, "rendering"), Port(entry)))]
                : []);
    }

    internal string ProjectPath(string presentationName)
    {
        string fileName = $"{Name}.{presentationName}.csproj";
        string presentation = Path.Combine(Root, "src", "Presentation");

        return Directory.EnumerateFiles(presentation, fileName, SearchOption.AllDirectories).SingleOrDefault()
            ?? throw new InvalidOperationException(
                $"{FileName} records the presentation project {presentationName}, but no {fileName} is under {presentation}.");
    }

    private static JsonElement[] Entries(JsonElement parent, string property) =>
        parent.TryGetProperty(property, out var entries) ? [.. entries.EnumerateArray()] : [];

    private static int? Port(JsonElement entry) =>
        entry.TryGetProperty("port", out var port) ? port.GetInt32() : null;

    private static string Text(JsonElement entry, string property) =>
        entry.TryGetProperty(property, out var value) && value.GetString() is { Length: > 0 } text
            ? text
            : throw new InvalidOperationException($"An entry of {FileName} has no {property}.");
}

internal sealed record PresentationProject(string Name, string Framework);

internal sealed record WebApp(string Name, string Framework, string Rendering, int? Port);

using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using CleanArch.Tooling.Failures;

namespace CleanArch.Tooling.Manifests;

public sealed record NuGetPackage(string File, string Id, string Version, string? Repository)
{
    public const string ContentFolder = "content/";

    private const string _manifest = ".template.config/template.json";

    public static NuGetPackage Open(string nupkg)
    {
        using ZipArchive archive = ZipFile.OpenRead(nupkg);
        ZipArchiveEntry nuspec = archive.Entries.FirstOrDefault(entry => !entry.FullName.Contains('/') && entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
            ?? throw new LauncherException($"{nupkg} holds no .nuspec, so it is not a NuGet package.");
        XElement metadata = XDocument.Parse(Text(nuspec)).Root!.Elements().First(element => element.Name.LocalName == "metadata");
        string? Meta(string name) => metadata.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value;
        string? repository = metadata.Elements().FirstOrDefault(element => element.Name.LocalName == "repository")?.Attribute("url")?.Value ?? Meta("projectUrl");

        return new NuGetPackage(nupkg, Meta("id") ?? string.Empty, Meta("version") ?? string.Empty, repository);
    }

    public static string? Entry(string nupkg, string path)
    {
        using ZipArchive archive = ZipFile.OpenRead(nupkg);
        return archive.GetEntry(path) is { } entry ? Text(entry) : null;
    }

    public string? Entry(string path) => Entry(File, path);

    public TemplateSource Content() => TemplateSource.FromPath(File);

    public IReadOnlyList<string> TemplateFolders()
    {
        using ZipArchive archive = ZipFile.OpenRead(File);

        return [.. archive.Entries
            .Select(entry => entry.FullName)
            .Where(path => path.StartsWith(ContentFolder, StringComparison.Ordinal) && path.EndsWith(_manifest, StringComparison.Ordinal) && (path.Length == ContentFolder.Length + _manifest.Length || path[^(_manifest.Length + 1)] == '/'))
            .Select(path => path[ContentFolder.Length..^"template.json".Length])];
    }

    public IEnumerable<(string Path, string Text)> ContentFiles()
    {
        using ZipArchive archive = ZipFile.OpenRead(File);

        foreach (ZipArchiveEntry entry in archive.Entries.Where(entry => entry.FullName.StartsWith(ContentFolder, StringComparison.Ordinal) && entry.Length > 0))
        {
            yield return (entry.FullName[ContentFolder.Length..], Text(entry));
        }
    }

    private static string Text(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}

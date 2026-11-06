using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CleanArch.Tooling.Failures;

namespace CleanArch.Cli.UpgradeReport;

internal sealed record TemplatePackage(
    string Version,
    IReadOnlyDictionary<string, PackagedTemplate> Templates,
    string? Changelog,
    string? Repository,
    IReadOnlySet<string> HeldGuids,
    bool DrawsGuids)
{
    private static readonly Regex _templateManifest = new(@"^content/(?:.+/)?\.template\.config/template\.json$", RegexOptions.None, TimeSpan.FromSeconds(2));

    public static TemplatePackage Read(string nupkg)
    {
        using ZipArchive archive = ZipFile.OpenRead(nupkg);
        XElement metadata = Metadata(archive, nupkg);
        string? repository = metadata.Elements().FirstOrDefault(element => element.Name.LocalName == "repository")?.Attribute("url")?.Value ?? Meta(metadata, "projectUrl");
        ZipArchiveEntry[] manifests = [.. archive.Entries.Where(entry => _templateManifest.IsMatch(entry.FullName))];

        return new TemplatePackage(
            Meta(metadata, "version") ?? string.Empty,
            TemplatesIn(archive, manifests),
            archive.GetEntry("CHANGELOG.md") is { } log ? Text(log) : null,
            repository,
            HeldGuidsOf(archive),
            manifests.Any(entry => JsonNode.Parse(Text(entry))?["guids"] is JsonArray { Count: > 0 }));
    }

    public static (string Id, string Version) Identify(string nupkg)
    {
        using ZipArchive archive = ZipFile.OpenRead(nupkg);
        XElement metadata = Metadata(archive, nupkg);

        return (Meta(metadata, "id") ?? string.Empty, Meta(metadata, "version") ?? string.Empty);
    }

    public PackagedTemplate Require(string identity, string version) =>
        Templates.GetValueOrDefault(identity)
        ?? throw new LauncherException(
            $"Version {version} of the package holds no template of identity {identity}, so the project cannot be generated from it the way "
            + "cleanarch.json describes; compare by hand.");

    private static XElement Metadata(ZipArchive archive, string nupkg)
    {
        ZipArchiveEntry nuspec = archive.Entries.FirstOrDefault(entry => !entry.FullName.Contains('/') && entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
            ?? throw new LauncherException($"{nupkg} holds no .nuspec, so it is not a NuGet package.");

        return XDocument.Parse(Text(nuspec)).Root!.Elements().First(element => element.Name.LocalName == "metadata");
    }

    private static string? Meta(XElement metadata, string name) =>
        metadata.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value;

    private static HashSet<string> HeldGuidsOf(ZipArchive archive) =>
        new(
            archive.Entries
                .Where(entry => entry.FullName.StartsWith("content/", StringComparison.Ordinal) && entry.Length > 0)
                .SelectMany(entry => CanonicalText.Guids(Text(entry))),
            StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, PackagedTemplate> TemplatesIn(ZipArchive archive, IEnumerable<ZipArchiveEntry> manifests)
    {
        var templates = new Dictionary<string, PackagedTemplate>(StringComparer.Ordinal);
        foreach (PackagedTemplate template in manifests.Select(entry => Template(archive, entry)))
        {
            templates[template.Identity] = template;
        }

        return templates;
    }

    private static PackagedTemplate Template(ZipArchive archive, ZipArchiveEntry entry)
    {
        JsonObject manifest = JsonNode.Parse(Text(entry))!.AsObject();
        string hostPath = entry.FullName[..^"template.json".Length] + "dotnetcli.host.json";
        JsonObject? host = archive.GetEntry(hostPath) is { } hostEntry ? JsonNode.Parse(Text(hostEntry))!.AsObject() : null;
        Dictionary<string, string> longNames = (host?["symbolInfo"] as JsonObject ?? [])
            .Where(symbol => symbol.Value?["longName"]?.GetValue<string>() is { Length: > 0 })
            .ToDictionary(symbol => symbol.Key, symbol => symbol.Value!["longName"]!.GetValue<string>(), StringComparer.Ordinal);
        string identity = manifest["identity"]?.GetValue<string>() ?? entry.FullName;
        string shortName = manifest["shortName"] switch
        {
            JsonArray names => names[0]!.GetValue<string>(),
            JsonNode name => name.GetValue<string>(),
            _ => identity,
        };

        return new PackagedTemplate(identity, shortName, manifest["symbols"] as JsonObject ?? [], manifest["postActions"] as JsonArray ?? [], longNames);
    }

    private static string Text(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }
}

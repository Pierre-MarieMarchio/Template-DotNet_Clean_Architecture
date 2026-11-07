using System.Text.Json;
using CleanArch.Tooling.Failures;
using CleanArch.Tooling.Manifests;

namespace CleanArch.Cli.UpgradeReport;

internal sealed record TemplatePackage(
    string Version,
    IReadOnlyDictionary<string, PackagedTemplate> Templates,
    string? Changelog,
    string? Repository,
    IReadOnlySet<string> HeldGuids,
    bool DrawsGuids)
{
    public static TemplatePackage Read(string nupkg)
    {
        NuGetPackage package = NuGetPackage.Open(nupkg);
        TemplateSource content = package.Content();
        var templates = new Dictionary<string, PackagedTemplate>(StringComparer.Ordinal);
        foreach (string folder in package.TemplateFolders())
        {
            TemplateManifest manifest = ManifestIn(nupkg, content, folder);
            string identity = manifest.Identity ?? NuGetPackage.ContentFolder + folder + "template.json";
            templates[identity] = new PackagedTemplate(identity, manifest);
        }

        return new TemplatePackage(
            package.Version,
            templates,
            package.Entry("CHANGELOG.md"),
            package.Repository,
            new HashSet<string>(package.ContentFiles().SelectMany(file => CanonicalText.Guids(file.Text)), StringComparer.OrdinalIgnoreCase),
            templates.Values.Any(template => template.Manifest.DeclaresGuids));
    }

    public PackagedTemplate Require(string identity, string version) =>
        Templates.GetValueOrDefault(identity)
        ?? throw new LauncherException(
            $"Version {version} of the package holds no template of identity {identity}, so the project cannot be generated from it the way "
            + "cleanarch.json describes; compare by hand.");

    private static TemplateManifest ManifestIn(string nupkg, TemplateSource content, string folder)
    {
        try
        {
            return content.ManifestIn(folder);
        }
        catch (Exception unreadable) when (unreadable is JsonException or FormatException)
        {
            throw new LauncherException(
                $"{nupkg} holds a {NuGetPackage.ContentFolder}{folder}template.json the report cannot read: {unreadable.Message} "
                + "The report cannot generate the project from that version: compare the versions by hand.");
        }
    }
}

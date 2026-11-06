using System.Text.Json;
using System.Text.Json.Nodes;
using CleanArch.Tooling.Failures;

namespace CleanArch.Cli.UpgradeReport;

internal static class Releases
{
    public static List<Release> Local(IReadOnlyList<string> sources, string package)
    {
        var releases = new List<Release>();

        foreach (string source in sources.Select(Path.GetFullPath))
        {
            foreach (string file in PackagesIn(source))
            {
                (string id, string version) = TemplatePackage.Identify(file);

                if (string.Equals(id, package, StringComparison.OrdinalIgnoreCase))
                {
                    releases.Add(new Release(version, file));
                }
            }
        }

        return releases;
    }

    public static IReadOnlyList<Release> NuGet(ReportProcesses processes, string root, string package)
    {
        (int exit, string output, string errors) = processes.Capture(root, "dotnet", "package", "search", package, "--exact-match", "--prerelease", "--format", "json");
        JsonNode result = (exit == 0 ? Parsed(output) : null)
            ?? throw new LauncherException(
                $"'dotnet package search {package}' failed, so the versions of the package are unknown: {(output + errors).Trim()} "
                + "Check the NuGet sources, or name a package with --source.");

        string[] problems = [.. (result["problems"] as JsonArray ?? []).Select(problem => problem?.ToJsonString() ?? string.Empty)];

        if (problems.Length > 0)
        {
            throw new LauncherException(
                $"'dotnet package search {package}' could not read every NuGet source: {string.Join("; ", problems)}");
        }

        return [.. (result["searchResult"] as JsonArray ?? [])
            .SelectMany(source => source?["packages"] as JsonArray ?? [])
            .Where(found => string.Equals(found?["id"]?.GetValue<string>(), package, StringComparison.OrdinalIgnoreCase))
            .Select(found => new Release(found!["version"]!.GetValue<string>(), null))];
    }

    public static (Release From, Release To) Choose(string current, string? to, IReadOnlyList<Release> catalog, string package)
    {
        string held = catalog.Count == 0
            ? "no version"
            : string.Join(", ", catalog.Select(release => release.Version).Distinct(StringComparer.OrdinalIgnoreCase).Order(SemanticVersion.Order));

        if (!SemanticVersion.IsVersion(current))
        {
            throw new LauncherException($"cleanarch.json records the version '{current}', which is not a version a package is published under.");
        }

        Release from = Find(catalog, current)
            ?? throw new LauncherException(
                $"Version {current} of {package}, the one cleanarch.json records, is not found: the sources hold {held}. "
                + "--source names a copy of that package.");

        return (from, to is null ? Latest(current, catalog, package, held) : Named(current, to, catalog, package, held));
    }

    private static IEnumerable<string> PackagesIn(string source)
    {
        if (Directory.Exists(source))
        {
            return Directory.EnumerateFiles(source, "*.nupkg", SearchOption.AllDirectories);
        }

        return File.Exists(source) ? [source] : throw new LauncherException($"--source {source} is neither a .nupkg nor a folder.");
    }

    private static JsonNode? Parsed(string output)
    {
        try
        {
            return JsonNode.Parse(output);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Release? Find(IReadOnlyList<Release> catalog, string version) =>
        catalog.FirstOrDefault(release => SemanticVersion.IsVersion(release.Version) && SemanticVersion.Compare(release.Version, version) == 0);

    private static Release Named(string current, string to, IReadOnlyList<Release> catalog, string package, string held)
    {
        if (!SemanticVersion.IsVersion(to))
        {
            throw new LauncherException($"--to '{to}' is not a version, such as 2.1.0 or 2.1.0-rc.1.");
        }

        Release target = Find(catalog, to)
            ?? throw new LauncherException($"Version {to} of {package} is not found: the sources hold {held}.");

        return SemanticVersion.Compare(target.Version, current) > 0
            ? target
            : throw new LauncherException($"--to {to} is not newer than {current}, the version cleanarch.json records.");
    }

    private static Release Latest(string current, IReadOnlyList<Release> catalog, string package, string held) =>
        catalog
            .Where(release => SemanticVersion.IsStable(release.Version))
            .Where(release => SemanticVersion.Compare(release.Version, current) > 0)
            .MaxBy(release => release.Version, SemanticVersion.Order)
        ?? throw new LauncherException(
            $"No stable version of {package} is newer than {current}: the sources hold {held}. --to names a prerelease.");
}

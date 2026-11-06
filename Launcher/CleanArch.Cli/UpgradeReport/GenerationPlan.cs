using System.Globalization;
using System.Text.RegularExpressions;

namespace CleanArch.Cli.UpgradeReport;

internal sealed record GenerationPlan(IReadOnlyList<(string Symbol, string Value)> Generation, IReadOnlyList<Replay> Replays)
{
    private static readonly (string Framework, string Name)[] _generatedHosts =
    [
        ("api", "Api"), ("blazor-server", "Portal"), ("mcp", "Mcp"), ("avalonia", "Desktop"), ("wpf", "Desktop"), ("worker", "Worker"),
    ];

    public static GenerationPlan Of(Provenance project)
    {
        (string Symbol, string Value)[] recorded = [.. project.Options];
        var generation = new List<(string Symbol, string Value)>(recorded);
        var replays = new List<Replay>();

        WebApp[] apps = [.. project.Web?.Apps ?? []];
        bool firstAppGenerated = apps.Length > 0 && project.Web!.Scope == ScopeOf(project.Name);
        if (firstAppGenerated)
        {
            generation.AddRange([("web", apps[0].Framework), ("webName", apps[0].Name), ("webAudience", apps[0].Audience), ("webRendering", apps[0].Rendering)]);
        }

        replays.AddRange(apps.Skip(firstAppGenerated ? 1 : 0).Select(app => WebAppReplay(project, app, recorded)));
        replays.AddRange((project.Web?.Packages ?? []).Select(package =>
            new Replay(PackagedTemplate.WebPackageItem, package, [("scope", project.Web!.Scope)], [.. recorded], "Src/Web/packages/" + package)));

        foreach (PresentationProject host in project.Presentation)
        {
            if (_generatedHosts.Contains((host.Framework, host.Name)))
            {
                generation.Add(("presentation", host.Framework));
            }
            else
            {
                replays.Add(new Replay(
                    PackagedTemplate.PresentationItem,
                    host.Name,
                    [("framework", host.Framework), ("projectName", project.Name)],
                    [.. recorded],
                    "Src/Presentation/*." + host.Name));
            }
        }

        return new GenerationPlan(generation, replays);
    }

    private static string ScopeOf(string name) =>
        Regex.Replace(name.ToLowerInvariant(), @"[.\s_]+", "-", RegexOptions.None, TimeSpan.FromSeconds(2));

    private static Replay WebAppReplay(Provenance project, WebApp app, (string Symbol, string Value)[] recorded) =>
        new(
            PackagedTemplate.WebAppItem,
            app.Name,
            [("framework", app.Framework), ("audience", app.Audience), ("rendering", app.Rendering), .. PortOf(app), ("scope", project.Web!.Scope), ("projectName", project.Name)],
            [.. recorded],
            "Src/Web/apps/" + app.Name);

    private static (string, string)[] PortOf(WebApp app) =>
        app.Port is { } port ? [("port", port.ToString(CultureInfo.InvariantCulture))] : [];
}

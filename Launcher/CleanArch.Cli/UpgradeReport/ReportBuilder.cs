using CleanArch.Tooling.Failures;

namespace CleanArch.Cli.UpgradeReport;

internal sealed class ReportBuilder(ReportProcesses processes)
{
    public Report Build(string root, Provenance project, UpgradeRequest request)
    {
        RefuseWhatTheReportCannotCompare(root, request);
        IReadOnlyList<Release> catalog = request.Sources.Count > 0
            ? Releases.Local(request.Sources, project.Package)
            : Releases.NuGet(processes, root, project.Package);
        (Release from, Release to) = Releases.Choose(project.Version, request.To, catalog, project.Package);

        string scratch = Directory.CreateTempSubdirectory("upgrade-report-").FullName;
        try
        {
            return Compare(root, project, request, (from, to), scratch);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    private Report Compare(string root, Provenance project, UpgradeRequest request, (Release From, Release To) versions, string scratch)
    {
        var generator = new Generator(processes, root, project, scratch);
        Generation older = generator.Generate(versions.From);
        Generation newer = generator.Generate(versions.To);

        List<string> decisions = [.. OptionChanges.Between(older.Content, newer.Content, project, versions.To.Version), .. older.Notes, .. newer.Notes];
        string[] projectFiles = ProjectTree.ProjectFiles(processes, root);
        ProjectTree.RequireTheProjectsPaths(older.Tree, root, projectFiles, project.Name);

        var trees = new Trees(older.Tree, newer.Tree, root);
        Comparison comparison = TreeComparison.Compare(
            trees, projectFiles, project.Name, JoinedPaths.Of(older.Tree, newer.Tree, GenerationPlan.Of(project)), HeldGuids(older.Content, newer.Content));
        string[] diffs = [.. comparison.Findings
            .Where(finding => finding.Kind == FindingKind.Merge)
            .Select(finding => TemplateDiff.Of(processes, scratch, trees, finding))];
        string changelog = Changelog.Of(newer.Content, versions.From.Version, versions.To.Version);

        if (request.Keep is { } folder)
        {
            Keep(folder, [(older.Tree, versions.From.Version), (newer.Tree, versions.To.Version)]);
        }

        var report = new Report(project.Name, versions.From.Version, versions.To.Version, decisions, comparison, diffs, changelog, request.Json, request.Keep);

        if (request.Json is { } file)
        {
            ReportWriter.WriteJson(report, file);
        }

        return report;
    }

    private static void RefuseWhatTheReportCannotCompare(string root, UpgradeRequest request)
    {
        if (File.Exists(Path.Combine(root, ".template.config", "template.json")))
        {
            throw new LauncherException(
                $"{root} is the checkout the package is packed from, not a project generated from it: there is no generated project to compare.");
        }

        if (request.Json is { } json && IsInside(json, root))
        {
            throw new LauncherException($"--json {json} is inside the project, and the report writes nothing there: name a file outside it.");
        }

        if (request.Keep is { } keep && (IsInside(keep, root) || Path.GetFullPath(keep) == Path.GetFullPath(root)))
        {
            throw new LauncherException($"--keep {keep} is inside the project, and the report writes nothing there: name a folder outside it.");
        }
    }

    private static HashSet<string>? HeldGuids(TemplatePackage older, TemplatePackage newer) =>
        older.DrawsGuids || newer.DrawsGuids
            ? new HashSet<string>(older.HeldGuids.Concat(newer.HeldGuids), StringComparer.OrdinalIgnoreCase)
            : null;

    private static void Keep(string folder, IEnumerable<(string Tree, string Version)> generations)
    {
        foreach ((string tree, string version) in generations)
        {
            string target = Path.Combine(folder, version);

            if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
            {
                throw new LauncherException($"--keep {folder} holds {version} already, which the report does not overwrite: name another folder, or empty it.");
            }

            CopyTree(tree, target);
        }
    }

    private static void CopyTree(string source, string target)
    {
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string copy = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }
    }

    private static bool IsInside(string path, string root) =>
        Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.Ordinal);
}

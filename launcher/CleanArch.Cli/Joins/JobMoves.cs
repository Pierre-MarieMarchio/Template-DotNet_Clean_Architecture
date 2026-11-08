using System.Text.RegularExpressions;
using CleanArch.Tooling;

namespace CleanArch.Cli.Joins;

internal sealed record FileMove(string From, string To);

internal sealed record NamespaceRename(string From, string To);

internal static class JobMoves
{
    public const string Worker = "Worker";

    public static IReadOnlyList<FileMove> Of(LocatedProject project)
    {
        string root = project.Root;
        string api = Folder(project, "src", "Api");
        string apiTests = Folder(project, "tests", "Api.UnitTests");
        string worker = Folder(project, "src", Worker);
        string workerTests = Folder(project, "tests", Worker + ".UnitTests");

        FileMove[] moves =
        [
            .. Moves(root, Path.Combine(api, "Common", "Jobs"), api, worker),
            .. FeatureJobs(api).SelectMany(folder => Moves(root, folder, api, worker)),
            .. FeatureJobs(apiTests).SelectMany(folder => Moves(root, folder, apiTests, workerTests)),
        ];

        string[] taken = [.. moves.Where(move => File.Exists(Path.Combine(root, move.To))).Select(move => move.To)];
        return taken.Length == 0
            ? moves
            : throw new JoinRefusedException(
                $"The worker holds these files already, which the API's jobs would replace, so no file was changed: {string.Join(", ", taken)}.");
    }

    public static IReadOnlyList<NamespaceRename> Namespaces(LocatedProject project, IEnumerable<FileMove> moves)
    {
        string product = project.File.Name;
        string apiFolder = $"src/Presentation/{ProjectNames.FolderOf(product, "Api")}/";
        string apiTestsFolder = $"tests/Presentation/{ProjectNames.FolderOf(product, "Api.UnitTests")}/";

        return [.. moves
            .Select(move => Namespace(product, move.From, apiFolder, "Api") ?? Namespace(product, move.From, apiTestsFolder, "Api.UnitTests"))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(space => new NamespaceRename(space, WorkerNamespace(product, space)))];
    }

    public static string WorkerNamespace(string product, string space) =>
        $"{product}.{Worker}{space[$"{product}.Api".Length..]}";

    public static string Renamed(string text, IEnumerable<NamespaceRename> namespaces) =>
        namespaces.Aggregate(text, (current, space) => Regex.Replace(
            current, $@"(?<![\w.]){Regex.Escape(space.From)}(?!\w)", _ => space.To, RegexOptions.None, TimeSpan.FromSeconds(5)));

    private static string? Namespace(string product, string from, string folder, string project) =>
        from.StartsWith(folder, StringComparison.Ordinal) && from[folder.Length..].Split('/') is var parts && Array.IndexOf(parts, "Jobs") is > 0 and int jobs
            ? $"{product}.{project}.{string.Join('.', parts[..(jobs + 1)])}"
            : null;

    private static string Folder(LocatedProject project, string tree, string part) =>
        Path.Combine(project.Root, tree, "Presentation", ProjectNames.FolderOf(project.File.Name, part));

    private static IEnumerable<FileMove> Moves(string root, string folder, string from, string to) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Select(file => new FileMove(ProjectPath.Relative(root, file), ProjectPath.Relative(root, Path.Combine(to, Path.GetRelativePath(from, file)))))
            : [];

    private static IEnumerable<string> FeatureJobs(string project) =>
        Directory.Exists(Path.Combine(project, "Features"))
            ? Directory.EnumerateDirectories(Path.Combine(project, "Features")).Select(feature => Path.Combine(feature, "Jobs"))
            : [];
}

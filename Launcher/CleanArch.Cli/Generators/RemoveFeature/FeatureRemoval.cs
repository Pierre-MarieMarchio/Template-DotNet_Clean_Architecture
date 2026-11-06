using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CleanArch.Cli.Generators.Shared;
using static CleanArch.Cli.Generators.Shared.ProjectFolders;
using static CleanArch.Cli.Generators.Shared.SourceText;

namespace CleanArch.Cli.Generators.RemoveFeature;

internal static class FeatureRemoval
{
    internal const string Usage =
        "Usage: dotnet cleanarch remove feature <Features> [--apply]\n"
        + "  <Features>  the folder of the feature, PascalCase and plural, e.g. Widgets\n"
        + "  --apply     remove the feature its manifest records, edited or not: delete its files and its page,\n"
        + "              undo each edit the generators recorded, then build, add the Remove<Features> migration\n"
        + "              and rewrite the OpenAPI reference and what is written from it. It refuses, writing\n"
        + "              nothing, while a file it would delete or change is not committed, a model change waits\n"
        + "              for a migration, or code outside the feature still uses it. Without --apply, the\n"
        + "              command only reports and writes nothing.";

    internal static readonly TimeSpan _timeout = TimeSpan.FromSeconds(2);

    internal static readonly Regex[] _sharedPaths =
    [
        Rx(@"^Src/Application/[^/]+\.Application\.Core/Common/Ports/(IDeletedAccountDataPolicy|IUserAccountDirectory)\.cs$"),
        Rx(@"^Src/Application/[^/]+\.Application\.Core/Features/DeletedAccounts/UseCases/Commands/SweepDeletedAccountData/SweepDeletedAccountData\.cs$"),
        Rx(@"^Src/Presentation/[^/]+/Features/BackgroundJobs/Jobs/DeletedAccountSweep(JobsExtensions|Options)\.cs$"),
        Rx(@"^Src/Presentation/[^/]+/Features/Auth/Composition/AccountUserAccountDirectory\.cs$"),
        Rx(@"^Src/Presentation/[^/]+/Features/ExternalIdentity/Composition/ExternalIdentityUserAccountDirectory\.cs$"),
    ];

    internal static readonly Regex _sharedFolder = Rx(@"^(Src/Application/[^/]+\.Application\.Core/Features/DeletedAccounts)/");

    internal static readonly Regex _sharedUse = Rx(
        @"\b(IDeletedAccountDataPolicy|IUserAccountDirectory|SweepDeletedAccountDataCommand(Handler)?|AddDeletedAccountSweep(Jobs)?|DeletedAccountSweepOptions|AccountUserAccountDirectory|ExternalIdentityUserAccountDirectory)\b|\.Features\.DeletedAccounts\b");

    internal static readonly Regex _consumer = Rx(
        @"^(Src|Tests)/[^/]+/[^/]+/Features/[A-Z][A-Za-z0-9]*/Services/Consumers/(?<base>[A-Z][A-Za-z0-9]*)/[A-Z][A-Za-z0-9]*On\k<base>Consumer(Tests)?\.cs$");

    internal static readonly HashSet<string> _sharedHosts = new(StringComparer.Ordinal)
    {
        "DeletedAccounts", "Auth", "ExternalIdentity", "BackgroundJobs",
    };

    internal static int Run(RemoveFeatureRequest request, GenerationContext context) =>
        CSharpNames.PascalCase.IsMatch(request.Features)
            ? new Removal(Analyze(context.Root, request.Features, context.Executor), context).Generate()
            : Refuse(context.Error, "Name the feature folder, PascalCase and plural, e.g. Widgets.");

    internal static int Refuse(TextWriter error, string problem)
    {
        error.WriteLine($"error: {problem}");
        error.WriteLine();
        error.WriteLine(Usage);

        return 1;
    }

    internal static Analysis Analyze(string root, string features, IExecutor executor)
    {
        var blockers = new List<string>();
        var manifest = ManifestOf(root, features, blockers);
        var (owned, modified, missing) = Inventory(root, manifest);
        var shared = manifest?.Shared ?? [];
        var folders = Folders(root, features, manifest, owned, shared);
        string? page = manifest?.Page;
        var listed = owned.Append(page).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var undone = new Dictionary<string, (byte[] Original, byte[] After)>(StringComparer.Ordinal);
        var dropped = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        var undos = manifest is null ? [] : RemovalUndos.PlanUndos(root, manifest.Edits, RemovalUndos.ForeignLines(root, features), undone, dropped);
        var lines = new LineReader(root, dropped);
        var hits = RemovalScan.ScanCode(root, features, manifest?.Type, manifest is null ? [] : RemovalScan.DeclaredTypes(root, manifest, manifest.Type), folders, listed, lines.Read);
        var notes = manifest is null ? [] : RemovalScan.ScanNotes(root, features, manifest.Type, RemovalScan.Sections(root, manifest), folders, listed, lines.Read);
        var dependents = new List<Dependent>();
        var uses = new List<Hit>();

        blockers.AddRange(Displaced(undos));
        blockers.AddRange(CodeBlockers(features, hits));

        if (features == "Auth")
        {
            blockers.Add("Auth is a module with its own projects, database context and host composition; it is never removed by this command.");
        }

        if (shared.Count > 0)
        {
            SharedDependents(root, features, listed, dependents, uses, blockers, lines.Read);
        }

        var analysis = new Analysis(
            features, manifest?.Type, manifest is not null, folders, owned, modified, missing, Extra(root, folders, listed), undos, undone, hits, notes, blockers, shared, dependents, uses,
            page, page is not null && !File.Exists(Path.Combine(root, page)), manifest is null ? null : MigrationOf(root, features, manifest), manifest?.AddedFields ?? []);

        if (manifest is not null && blockers.Count == 0)
        {
            blockers.AddRange(GitProblems(root, [.. analysis.Deleted, .. undone.Keys], executor));
        }

        return analysis;
    }

    private static Manifest? ManifestOf(string root, string features, List<string> blockers)
    {
        string manifestPath = FeatureManifest.PathOf(features);

        if (File.Exists(Path.Combine(root, manifestPath)))
        {
            return RemovalManifest.ReadManifest(root, features, blockers);
        }

        blockers.Add($"'{manifestPath}' does not exist: the generator did not write {features} here, or the manifest was deleted. Only a manifest says what is the feature's to remove.");

        return null;
    }

    private static (List<string> Owned, List<string> Modified, List<string> Missing) Inventory(string root, Manifest? manifest)
    {
        if (manifest is null)
        {
            return ([], [], []);
        }

        var modified = new List<string>();
        var missing = new List<string>();

        foreach (var entry in manifest.Files)
        {
            string full = Path.Combine(root, entry.Path);

            if (!File.Exists(full))
            {
                missing.Add(entry.Path);
            }
            else if (!string.Equals(FeatureManifest.Sha256(File.ReadAllBytes(full)), entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                modified.Add(entry.Path);
            }
        }

        return ([.. manifest.Files.Select(entry => entry.Path)], modified, missing);
    }

    private static List<string> Folders(string root, string features, Manifest? manifest, List<string> owned, List<string> shared)
    {
        var folders = manifest is not null
            ? RemovalScan.FeatureFolders([.. owned.Except(shared, StringComparer.Ordinal).Except(manifest.Consumers, StringComparer.Ordinal)], features)
            : RemovalScan.DiscoverFolders(root, features);

        foreach (string folder in shared.Select(path => _sharedFolder.Match(path)).Where(match => match.Success).Select(match => match.Groups[1].Value))
        {
            if (!folders.Contains(folder, StringComparer.Ordinal))
            {
                folders.Add(folder);
            }
        }

        return folders;
    }

    private static List<string> Extra(string root, List<string> folders, HashSet<string> listed)
    {
        var extra = folders
            .Select(folder => Path.Combine(root, folder))
            .Where(Directory.Exists)
            .SelectMany(full => Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
            .Select(file => Path.GetRelativePath(root, file).Replace('\\', '/'))
            .Where(relative => !listed.Contains(relative))
            .ToList();

        extra.Sort(StringComparer.Ordinal);

        return extra;
    }

    private static IEnumerable<string> Displaced(List<Undo> undos) =>
        undos.Where(undo => undo.State == UndoState.Displaced).Select(undo =>
            $"{undo.Edit.Path}: the {undo.Edit.Lines.Length} line(s) recorded {undo.Edit.Position} '{undo.Edit.Anchor.Trim()}' are no longer there as they were written, "
            + "so they cannot be undone exactly. Put them back as the manifest lists them, or remove them by hand and commit, then run this again.");

    private static IEnumerable<string> CodeBlockers(string features, List<Hit> hits)
    {
        if (hits.Count == 0)
        {
            yield break;
        }

        string[] others = [.. hits.Select(hit => FeatureOf(hit.Path)).OfType<string>().Where(other => other != features).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

        yield return
            $"{hits.Count} line(s) of code outside the feature, which no manifest records, still use its types, its namespace or its registration"
            + (others.Length > 0 ? $", in {string.Join(", ", others)}, which depend(s) on it" : string.Empty)
            + ": the build breaks once the feature is gone. Remove those uses, or remove those features first, and commit.";
    }

    private sealed class LineReader(string root, Dictionary<string, HashSet<int>> dropped)
    {
        internal string?[]? Read(string relative)
        {
            string full = Path.Combine(root, relative);

            if (!File.Exists(full))
            {
                return null;
            }

            string?[] lines = Decode(File.ReadAllBytes(full)).ReplaceLineEndings("\n").Split('\n');

            foreach (int line in dropped.GetValueOrDefault(relative) ?? [])
            {
                lines[line] = null;
            }

            return lines;
        }
    }

    internal static string? FeatureOf(string path)
    {
        int at = path.IndexOf("/Features/", StringComparison.Ordinal);

        return at < 0 ? null : path[(at + "/Features/".Length)..].Split('/')[0];
    }

    internal static IEnumerable<string> GitProblems(string root, List<string> paths, IExecutor executor)
    {
        if (paths.Count == 0)
        {
            yield break;
        }

        if (Git(executor, root, "rev-parse", "--is-inside-work-tree").Code != 0)
        {
            yield return "This tree is not a git work tree. --apply deletes and edits files that only git restore gives back, so it runs on a committed tree alone.";

            yield break;
        }

        string[] specs = [.. paths.Select(path => ":(literal)" + path)];
        var tracked = Git(executor, root, ["ls-files", "-z", "--", .. specs]).Output.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var changed = Git(executor, root, ["status", "--porcelain=v1", "-z", "--untracked-files=all", "--", .. specs]).Output
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(entry => entry.Length > 3)
            .Select(entry => entry[3..])
            .ToHashSet(StringComparer.Ordinal);
        string prefix = Git(executor, root, "rev-parse", "--show-prefix").Output.Trim();
        string[] unsafePaths = [.. paths.Where(path => !tracked.Contains(path) || changed.Contains(prefix + path)).Order(StringComparer.Ordinal)];

        if (unsafePaths.Length > 0)
        {
            yield return
                $"{unsafePaths.Length} file(s) --apply would delete or edit are not committed as they stand, and only git restore gives back what --apply removes: "
                + "commit them, or restore them, then run this again:\n"
                + string.Join("\n", unsafePaths.Select(path => $"      {path}"));
        }
    }

    internal static (int Code, string Output) Git(IExecutor executor, string root, params string[] arguments) =>
        executor.Read(new TextEdits.Command($"git {string.Join(' ', arguments)}", root, "git", arguments));

    internal static string? Persistence(string root)
    {
        string infrastructure = Full(root, $"{Src}/Infrastructure");

        return Directory.Exists(infrastructure)
            ? Directory.EnumerateDirectories(infrastructure, "*.Infrastructure.Persistence").Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')).FirstOrDefault()
            : null;
    }

    internal static Migration? MigrationOf(string root, string features, Manifest manifest)
    {
        if (Persistence(root) is not { } persistence || !Directory.Exists(Path.Combine(root, persistence, "Migrations")))
        {
            return null;
        }

        string folder = Path.Combine(root, persistence, "Migrations");
        string? added = Directory.EnumerateFiles(folder, $"*_Add{features}.cs").Order(StringComparer.Ordinal).FirstOrDefault();

        if (added is null)
        {
            return null;
        }

        string name = $"Remove{features}";

        for (int again = 2; Directory.EnumerateFiles(folder, $"*_{name}.cs").Any(); again++)
        {
            name = $"Remove{features}{again}";
        }

        var constant = new Regex($@"\bconst\s+string\s+{features}Schema\s*=\s*""([^""]+)""", RegexOptions.None, _timeout);
        string? schema = manifest.Edits.SelectMany(edit => edit.Lines).Select(line => constant.Match(line)).FirstOrDefault(match => match.Success)?.Groups[1].Value;
        string migration = File.ReadAllText(added);

        if (schema is not null && !Regex.IsMatch(migration, $@"EnsureSchema\(\s*name:\s*""{Regex.Escape(schema)}""\s*\)", RegexOptions.None, _timeout))
        {
            schema = null;
        }

        return new Migration(persistence, name, schema);
    }

    internal static void SharedDependents(string root, string features, HashSet<string> own, List<Dependent> dependents, List<Hit> uses, List<string> blockers, Func<string, string?[]?> read)
    {
        ManifestDependents(root, features, dependents, blockers);
        SharedUses(root, features, own, dependents, uses, read);

        if (dependents.Count > 0)
        {
            blockers.Add(
                "The account-deletion files this manifest brought (IDeletedAccountDataPolicy, IUserAccountDirectory, the sweep, its job and the account directory) "
                + $"are still used by {string.Join(", ", dependents.Select(dependent => $"{dependent.Name} ({dependent.How})"))}. "
                + $"Deleting them would break those features' build. Remove those features first, or remove {features} by hand and keep these files "
                + "(docs/guides/removing-features.md, \"Remove a generated feature\").");
        }

        if (uses.Count > 0)
        {
            blockers.Add(
                $"{uses.Count} line(s) no manifest records still register the account-deletion files this manifest brought "
                + "(AddDeletedAccountSweep, AddDeletedAccountSweepJobs, the IUserAccountDirectory adapter, the architecture tests' double): undo them first.");
        }
    }

    private static void ManifestDependents(string root, string features, List<Dependent> dependents, List<string> blockers)
    {
        string manifests = Path.Combine(root, FeatureManifest.Folder);

        if (!Directory.Exists(manifests))
        {
            return;
        }

        foreach (string other in Directory.EnumerateFiles(manifests, "*.json").Order(StringComparer.Ordinal).Select(Path.GetFileNameWithoutExtension).OfType<string>().Where(other => other != features))
        {
            var manifest = FeatureManifest.Read(root, other, out string? problem);
            string? access = manifest?.Options?["access"] is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

            if (access == "owner")
            {
                dependents.Add(new Dependent(other, "generated, --access owner"));
            }
            else if (access is null)
            {
                blockers.Add($"'{FeatureManifest.PathOf(other)}' cannot be read ({problem ?? "it records no access"}), so nothing can tell whether {other} depends on the account-deletion files this manifest brought.");
            }
        }
    }

    private static void SharedUses(string root, string features, HashSet<string> own, List<Dependent> dependents, List<Hit> uses, Func<string, string?[]?> read)
    {
        foreach (string file in RemovalScan.CodeFiles(root))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');

            if (own.Contains(relative) || read(relative) is not { } lines)
            {
                continue;
            }

            for (int i = 0; i < lines.Length; i++)
            {
                string text = lines[i]?.Trim() ?? string.Empty;

                if (!text.StartsWith("//", StringComparison.Ordinal) && _sharedUse.IsMatch(text))
                {
                    SharedUse(relative, i + 1, text, features, dependents, uses);
                }
            }
        }
    }

    private static void SharedUse(string relative, int line, string text, string features, List<Dependent> dependents, List<Hit> uses)
    {
        string? folder = FeatureOf(relative);

        if (folder is null || _sharedHosts.Contains(folder))
        {
            uses.Add(new Hit(HitKind.Composition, relative, line, text));
        }
        else if (folder != features && !dependents.Any(dependent => dependent.Name == folder))
        {
            dependents.Add(new Dependent(folder, "no manifest"));
        }
    }

    internal static string PagePath(string features) => $"docs/features/{CSharpNames.Kebab(features)}.md";

    internal static Regex Rx(string pattern) => new(pattern, RegexOptions.None, _timeout);
}

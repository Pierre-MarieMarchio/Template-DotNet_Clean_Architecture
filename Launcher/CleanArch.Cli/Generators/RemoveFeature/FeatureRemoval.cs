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
        string manifestPath = FeatureManifest.PathOf(features);

        Manifest? manifest = null;

        if (File.Exists(Path.Combine(root, manifestPath)))
        {
            manifest = RemovalManifest.ReadManifest(root, features, blockers);
        }
        else
        {
            blockers.Add($"'{manifestPath}' does not exist: the generator did not write {features} here, or the manifest was deleted. Only a manifest says what is the feature's to remove.");
        }

        var owned = new List<string>();
        var modified = new List<string>();
        var missing = new List<string>();

        if (manifest is not null)
        {
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

            owned.AddRange(manifest.Files.Select(entry => entry.Path));
        }

        var shared = manifest?.Shared ?? [];

        var folders = manifest is not null
            ? RemovalScan.FeatureFolders([.. owned.Except(shared, StringComparer.Ordinal).Except(manifest.Consumers, StringComparer.Ordinal)], features)
            : RemovalScan.DiscoverFolders(root, features);

        foreach (string path in shared)
        {
            var match = _sharedFolder.Match(path);

            if (match.Success && !folders.Contains(match.Groups[1].Value, StringComparer.Ordinal))
            {
                folders.Add(match.Groups[1].Value);
            }
        }

        string? page = manifest?.Page;
        var listed = owned.ToHashSet(StringComparer.Ordinal);

        if (page is not null)
        {
            listed.Add(page);
        }

        var extra = new List<string>();

        foreach (string folder in folders)
        {
            string full = Path.Combine(root, folder);

            if (!Directory.Exists(full))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(root, file).Replace('\\', '/');

                if (!listed.Contains(relative))
                {
                    extra.Add(relative);
                }
            }
        }

        extra.Sort(StringComparer.Ordinal);

        var undone = new Dictionary<string, (byte[] Original, byte[] After)>(StringComparer.Ordinal);
        var dropped = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        var undos = manifest is null ? [] : RemovalUndos.PlanUndos(root, manifest.Edits, RemovalUndos.ForeignLines(root, features), undone, dropped);

        foreach (var undo in undos.Where(undo => undo.State == UndoState.Displaced))
        {
            blockers.Add(
                $"{undo.Edit.Path}: the {undo.Edit.Lines.Length} line(s) recorded {undo.Edit.Position} '{undo.Edit.Anchor.Trim()}' are no longer there as they were written, "
                + "so they cannot be undone exactly. Put them back as the manifest lists them, or remove them by hand and commit, then run this again.");
        }

        string?[]? Lines(string relative)
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

        var declared = manifest is null ? [] : RemovalScan.DeclaredTypes(root, manifest, manifest.Type);
        var sections = manifest is null ? [] : RemovalScan.Sections(root, manifest);
        var hits = RemovalScan.ScanCode(root, features, manifest?.Type, declared, folders, listed, Lines);
        var notes = manifest is null ? [] : RemovalScan.ScanNotes(root, features, manifest.Type, sections, folders, listed, Lines);

        if (hits.Count > 0)
        {
            string[] others = [.. hits.Select(hit => FeatureOf(hit.Path)).OfType<string>().Where(other => other != features).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

            blockers.Add(
                $"{hits.Count} line(s) of code outside the feature, which no manifest records, still use its types, its namespace or its registration"
                + (others.Length > 0 ? $", in {string.Join(", ", others)}, which depend(s) on it" : string.Empty)
                + ": the build breaks once the feature is gone. Remove those uses, or remove those features first, and commit.");
        }

        if (features == "Auth")
        {
            blockers.Add("Auth is a module with its own projects, database context and host composition; it is never removed by this command.");
        }

        var dependents = new List<Dependent>();
        var uses = new List<Hit>();

        if (shared.Count > 0)
        {
            SharedDependents(root, features, listed, dependents, uses, blockers, Lines);
        }

        var analysis = new Analysis(
            features, manifest?.Type, manifest is not null, folders, owned, modified, missing, extra, undos, undone, hits, notes, blockers, shared, dependents, uses,
            page, page is not null && !File.Exists(Path.Combine(root, page)), manifest is null ? null : MigrationOf(root, features, manifest), manifest?.AddedFields ?? []);

        if (manifest is not null && blockers.Count == 0)
        {
            blockers.AddRange(GitProblems(root, [.. analysis.Deleted, .. undone.Keys], executor));
        }

        return analysis;
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
        string manifests = Path.Combine(root, FeatureManifest.Folder);

        if (Directory.Exists(manifests))
        {
            foreach (string file in Directory.EnumerateFiles(manifests, "*.json").Order(StringComparer.Ordinal))
            {
                string other = Path.GetFileNameWithoutExtension(file);

                if (other == features)
                {
                    continue;
                }

                var manifest = FeatureManifest.Read(root, other, out string? problem);
                string? access = manifest?.Options?["access"] is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

                if (access == "owner")
                {
                    dependents.Add(new Dependent(other, "generated, --access owner"));
                }
                else if (access is null)
                {
                    string reason = problem ?? "it records no access";

                    blockers.Add($"'{FeatureManifest.PathOf(other)}' cannot be read ({reason}), so nothing can tell whether {other} depends on the account-deletion files this manifest brought.");
                }
            }
        }

        foreach (string top in new[] { "Src", "Tests" })
        {
            string start = Path.Combine(root, top);

            if (!Directory.Exists(start))
            {
                continue;
            }

            foreach (string file in RemovalScan.Walk(root, start, "*.cs"))
            {
                string relative = Path.GetRelativePath(root, file).Replace('\\', '/');

                if (own.Contains(relative) || read(relative) is not { } lines)
                {
                    continue;
                }

                for (int i = 0; i < lines.Length; i++)
                {
                    string text = lines[i]?.Trim() ?? string.Empty;

                    if (text.StartsWith("//", StringComparison.Ordinal) || !_sharedUse.IsMatch(text))
                    {
                        continue;
                    }

                    string? folder = FeatureOf(relative);

                    if (folder is null || _sharedHosts.Contains(folder))
                    {
                        uses.Add(new Hit(HitKind.Composition, relative, i + 1, text));
                    }
                    else if (folder != features && !dependents.Any(dependent => dependent.Name == folder))
                    {
                        dependents.Add(new Dependent(folder, "no manifest"));
                    }
                }
            }
        }

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

    internal static string PagePath(string features) => $"docs/features/{CSharpNames.Kebab(features)}.md";

    internal static Regex Rx(string pattern) => new(pattern, RegexOptions.None, _timeout);
}

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CleanArch.Cli.Generators.Shared;

namespace CleanArch.Cli.Generators.RemoveFeature;

internal static class FeatureRemoval
{
    private const string _usage =
        "Usage: dotnet cleanarch remove feature <Features> [--apply]\n"
        + "  <Features>  the folder of the feature, PascalCase and plural, e.g. Widgets\n"
        + "  --apply     remove the feature its manifest records, edited or not: delete its files and its page,\n"
        + "              undo each edit the generators recorded, then build, add the Remove<Features> migration\n"
        + "              and rewrite the OpenAPI reference and what is written from it. It refuses, writing\n"
        + "              nothing, while a file it would delete or change is not committed, a model change waits\n"
        + "              for a migration, or code outside the feature still uses it. Without --apply, the\n"
        + "              command only reports and writes nothing.";

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(2);

    private static readonly Regex _declaration = new(@"\b(?:(?:record\s+)?(?:class|struct)|interface|record|enum)\s+([A-Z]\w*)", RegexOptions.None, TimeSpan.FromSeconds(1));

    private static readonly Regex _pascalCase = new(@"^[A-Z][A-Za-z0-9]*$", RegexOptions.None, TimeSpan.FromSeconds(1));

    private static readonly Regex _sectionName = new(@"\bconst\s+string\s+SectionName\s*=\s*""([A-Z][A-Za-z0-9]*)""", RegexOptions.None, TimeSpan.FromSeconds(1));

    private static readonly HashSet<string> _skippedFolders = new(StringComparer.Ordinal)
    {
        ".git", ".vs", ".claude", "bin", "obj", "artifacts", "node_modules", "TestResults", "Generated", "Migrations", FeatureManifest.Folder,
    };

    private enum Kind
    {
        Composition,
        Model,
        Reference,
    }

    private enum State
    {
        Undo,
        Gone,
        Displaced,
    }

    private sealed record Hit(Kind Kind, string Path, int Line, string Text);

    private sealed record Note(string Group, string Path, int Line, string Text);

    private sealed record Undo(TextEdits.Edit Edit, State State);

    private sealed record Entry(string Path, string Sha256);

    private sealed record Manifest(
        string Type,
        List<Entry> Files,
        List<string> Shared,
        List<string> Consumers,
        string? Page,
        IReadOnlyList<TextEdits.Edit> Edits,
        IReadOnlyList<(string Declaration, string Migration, string[] Paths)> AddedFields);

    private sealed record Dependent(string Name, string How);

    private sealed record Migration(string Persistence, string Name, string? Schema);

    private sealed record Analysis(
        string Features,
        string? Type,
        bool HasManifest,
        List<string> Folders,
        List<string> Owned,
        List<string> Modified,
        List<string> Missing,
        List<string> Extra,
        List<Undo> Undos,
        Dictionary<string, (byte[] Original, byte[] After)> Undone,
        List<Hit> Hits,
        List<Note> Notes,
        List<string> Blockers,
        List<string> Shared,
        List<Dependent> Dependents,
        List<Hit> SharedUses,
        string? Page,
        bool PageGone,
        Migration? Migration,
        IReadOnlyList<(string Declaration, string Migration, string[] Paths)> AddedFields)
    {
        internal IEnumerable<string> Deleted =>
            Owned.Except(Missing, StringComparer.Ordinal)
                .Concat(Extra)
                .Concat(Page is not null && !PageGone ? new[] { Page } : [])
                .Concat(HasManifest ? new[] { FeatureManifest.PathOf(Features) } : []);
    }

    private static readonly Regex[] _sharedPaths =
    [
        Rx(@"^Src/Application/[^/]+\.Application\.Core/Common/Ports/(IDeletedAccountDataPolicy|IUserAccountDirectory)\.cs$"),
        Rx(@"^Src/Application/[^/]+\.Application\.Core/Features/DeletedAccounts/UseCases/Commands/SweepDeletedAccountData/SweepDeletedAccountData\.cs$"),
        Rx(@"^Src/Presentation/[^/]+/Features/BackgroundJobs/Jobs/DeletedAccountSweep(JobsExtensions|Options)\.cs$"),
        Rx(@"^Src/Presentation/[^/]+/Features/Auth/Composition/AccountUserAccountDirectory\.cs$"),
        Rx(@"^Src/Presentation/[^/]+/Features/ExternalIdentity/Composition/ExternalIdentityUserAccountDirectory\.cs$"),
    ];

    private static readonly Regex _sharedFolder = Rx(@"^(Src/Application/[^/]+\.Application\.Core/Features/DeletedAccounts)/");

    private static readonly Regex _sharedUse = Rx(
        @"\b(IDeletedAccountDataPolicy|IUserAccountDirectory|SweepDeletedAccountDataCommand(Handler)?|AddDeletedAccountSweep(Jobs)?|DeletedAccountSweepOptions|AccountUserAccountDirectory|ExternalIdentityUserAccountDirectory)\b|\.Features\.DeletedAccounts\b");

    private static readonly Regex _consumer = Rx(
        @"^(Src|Tests)/[^/]+/[^/]+/Features/[A-Z][A-Za-z0-9]*/Services/Consumers/(?<base>[A-Z][A-Za-z0-9]*)/[A-Z][A-Za-z0-9]*On\k<base>Consumer(Tests)?\.cs$");

    private static readonly HashSet<string> _sharedHosts = new(StringComparer.Ordinal)
    {
        "DeletedAccounts", "Auth", "ExternalIdentity", "BackgroundJobs",
    };

    internal static int Run(string[] args, TextWriter output, TextWriter error, Func<TextEdits.Command, int> run)
    {
        string? features = null;
        string root = Directory.GetCurrentDirectory();
        bool apply = false;

        for (int i = 0; i < args.Length; i++)
        {
            string argument = args[i];

            if (argument == "--apply")
            {
                apply = true;
            }
            else if (argument == "--root" && i + 1 < args.Length)
            {
                root = args[++i];
            }
            else if (argument.StartsWith('-') || features is not null)
            {
                return Refuse(error, $"'{argument}' is not understood here.");
            }
            else
            {
                features = argument;
            }
        }

        if (features is null || !_pascalCase.IsMatch(features))
        {
            return Refuse(error, "Name the feature folder, PascalCase and plural, e.g. Widgets.");
        }

        root = Path.GetFullPath(root);

        var analysis = Analyze(root, features);

        if (!analysis.HasManifest && analysis.Folders.Count == 0 && analysis.Hits.Count == 0)
        {
            output.WriteLine($"No feature folder called {features} is left under Src or Tests, and no manifest names it.");
            PrintNotes(output, analysis);

            return 0;
        }

        if (!apply)
        {
            PrintReport(output, root, analysis);
            output.WriteLine();
            output.WriteLine(
                analysis.Blockers.Count == 0
                    ? $"Nothing was changed. --apply removes what is listed above: dotnet run Tools/Tasks.cs remove-feature {features} --apply"
                    : "Nothing was changed. --apply would refuse, for the reasons listed under Blockers.");

            return 0;
        }

        if (analysis.Blockers.Count > 0)
        {
            PrintReport(output, root, analysis);
            error.WriteLine();
            error.WriteLine($"error: {features} was not removed, and nothing was changed: fix what Blockers lists, commit, then run this again.");

            if (analysis.Dependents.Count > 0)
            {
                error.WriteLine($"error: {features} holds the shared account-deletion files that {string.Join(", ", analysis.Dependents.Select(dependent => dependent.Name))} still depend on.");
            }

            return 1;
        }

        if (analysis.Migration is not null || HasInitialMigration(root))
        {
            var check = TextEdits.Task(root, "migration-check");

            output.WriteLine($"Checking that no model change waits for a migration: {check.Display}");
            output.Flush();

            if (run(check) != 0)
            {
                error.WriteLine(
                    $"error: `{check.Display}` failed: the business model holds a change no migration holds yet, or it could not be checked. "
                    + $"The migration that removes {features} would carry that change too, so put it in a migration of its own with "
                    + "`dotnet run Tools/Tasks.cs migration-add <Name>`, or undo it, then run this again.");
                error.WriteLine("Nothing was changed.");

                return 1;
            }
        }

        return Remove(root, analysis, output, error, run);
    }

    private static int Refuse(TextWriter error, string problem)
    {
        error.WriteLine($"error: {problem}");
        error.WriteLine();
        error.WriteLine(_usage);

        return 1;
    }

    private static Analysis Analyze(string root, string features)
    {
        var blockers = new List<string>();
        string manifestPath = FeatureManifest.PathOf(features);

        Manifest? manifest = null;

        if (File.Exists(Path.Combine(root, manifestPath)))
        {
            manifest = ReadManifest(root, features, blockers);
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
            ? FeatureFolders([.. owned.Except(shared, StringComparer.Ordinal).Except(manifest.Consumers, StringComparer.Ordinal)], features)
            : DiscoverFolders(root, features);

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
        var undos = manifest is null ? [] : PlanUndos(root, manifest.Edits, ForeignLines(root, features), undone, dropped);

        foreach (var undo in undos.Where(undo => undo.State == State.Displaced))
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

            string?[] lines = Decode(File.ReadAllBytes(full)).Split('\n');

            foreach (int line in dropped.GetValueOrDefault(relative) ?? [])
            {
                lines[line] = null;
            }

            return lines;
        }

        var declared = manifest is null ? [] : DeclaredTypes(root, manifest, manifest.Type);
        var sections = manifest is null ? [] : Sections(root, manifest);
        var hits = ScanCode(root, features, manifest?.Type, declared, folders, listed, Lines);
        var notes = manifest is null ? [] : ScanNotes(root, features, manifest.Type, sections, folders, listed, Lines);

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
            blockers.AddRange(GitProblems(root, [.. analysis.Deleted, .. undone.Keys]));
        }

        return analysis;
    }

    private static string? FeatureOf(string path)
    {
        int at = path.IndexOf("/Features/", StringComparison.Ordinal);

        return at < 0 ? null : path[(at + "/Features/".Length)..].Split('/')[0];
    }

    private static Dictionary<string, HashSet<string>> ForeignLines(string root, string features)
    {
        var foreign = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        string folder = Path.Combine(root, FeatureManifest.Folder);

        foreach (string file in Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.json") : [])
        {
            string other = Path.GetFileNameWithoutExtension(file);

            if (other == features || FeatureManifest.Read(root, other, out _) is not { } manifest)
            {
                continue;
            }

            foreach (var edit in manifest.Edits)
            {
                if (!foreign.TryGetValue(edit.Path, out var lines))
                {
                    foreign[edit.Path] = lines = new HashSet<string>(StringComparer.Ordinal);
                }

                lines.UnionWith(edit.Lines);
            }
        }

        return foreign;
    }

    private static List<Undo> PlanUndos(
        string root,
        IReadOnlyList<TextEdits.Edit> edits,
        Dictionary<string, HashSet<string>> foreign,
        Dictionary<string, (byte[] Original, byte[] After)> undone,
        Dictionary<string, HashSet<int>> dropped)
    {
        var states = new Dictionary<TextEdits.Edit, State>(ReferenceEqualityComparer.Instance);

        foreach (var group in edits.GroupBy(edit => edit.Path, StringComparer.Ordinal))
        {
            string full = Path.Combine(root, group.Key);

            if (!File.Exists(full))
            {
                foreach (var edit in group)
                {
                    states[edit] = State.Gone;
                }

                continue;
            }

            byte[] original = File.ReadAllBytes(full);
            bool bom = original is [0xEF, 0xBB, 0xBF, ..];
            string text = Encoding.UTF8.GetString(original, bom ? 3 : 0, original.Length - (bom ? 3 : 0));
            string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            bool finalNewline = text.EndsWith('\n');
            List<string> lines = [.. (finalNewline ? text[..^newline.Length] : text).Split(newline)];
            List<int> origin = [.. Enumerable.Range(0, lines.Count)];
            int count = lines.Count;
            var twins = group.CountBy(Twin, StringComparer.Ordinal).ToDictionary(StringComparer.Ordinal);

            foreach (var edit in group.Reverse())
            {
                var (state, positions, at) = Locate(lines, edit, twins[Twin(edit)]--, foreign.GetValueOrDefault(group.Key) ?? []);

                states[edit] = state;

                if (state != State.Undo)
                {
                    continue;
                }

                foreach (int position in Enumerable.Reverse(positions))
                {
                    lines.RemoveAt(position);
                    origin.RemoveAt(position);
                }

                lines.InsertRange(at, edit.Replaced);
                origin.InsertRange(at, edit.Replaced.Select(_ => -1));
            }

            byte[] after = [.. bom ? Encoding.UTF8.Preamble : [], .. Encoding.UTF8.GetBytes(string.Join(newline, lines) + (finalNewline ? newline : string.Empty))];

            if (!after.AsSpan().SequenceEqual(original))
            {
                undone[group.Key] = (original, after);
                dropped[group.Key] = [.. Enumerable.Range(0, count).Except(origin)];
            }
        }

        return [.. edits.Select(edit => new Undo(edit, states[edit]))];
    }

    private static string Twin(TextEdits.Edit edit) =>
        string.Join('\u0001', [edit.Anchor, edit.Position, .. edit.Lines, "\u0002", .. edit.Replaced]);

    private static (State State, int[] Positions, int At) Locate(List<string> lines, TextEdits.Edit edit, int twins, HashSet<string> foreign)
    {
        string[] added = edit.Lines;
        bool before = edit.Position == "before";
        int[] anchors = [.. Enumerable.Range(0, lines.Count).Where(index => lines[index] == edit.Anchor)];

        bool Holds(int at, string[] block) =>
            at >= 0 && at + block.Length <= lines.Count && lines.Skip(at).Take(block.Length).SequenceEqual(block, StringComparer.Ordinal);

        int[] Beside(string[] block) => [.. anchors.Select(index => before ? index - block.Length : index + 1).Where(at => Holds(at, block))];

        int[] Span(int at) => [.. Enumerable.Range(at, added.Length)];

        if (added.Length == 0)
        {
            return Beside(edit.Replaced).Length > 0 ? (State.Gone, [], -1)
                : anchors.Length == 1 || anchors.Length == twins ? (State.Undo, [], before ? anchors[0] : anchors[0] + 1)
                : (State.Displaced, [], -1);
        }

        int[] beside = Beside(added);

        if (beside.Length == 1 || (beside.Length > 0 && beside.Length == twins))
        {
            return (State.Undo, Span(beside[0]), beside[0]);
        }

        string[] visible = [.. added.Where(line => line.Trim().Length > 0)];

        if (beside.Length > twins || visible.Length == 0)
        {
            return (beside.Length > twins ? State.Displaced : State.Gone, [], -1);
        }

        int[]? Spread(int start)
        {
            var positions = new List<int> { start };

            for (int next = start + 1, index = 1; index < added.Length; next++)
            {
                if (next >= lines.Count)
                {
                    return null;
                }

                if (lines[next] == added[index])
                {
                    positions.Add(next);
                    index++;
                }
                else if (!foreign.Contains(lines[next]))
                {
                    return null;
                }
            }

            return [.. positions];
        }

        int[] contiguous = [.. Enumerable.Range(0, lines.Count).Where(at => Holds(at, added))];

        if (contiguous.Length == 1 || (contiguous.Length > 0 && contiguous.Length == twins))
        {
            return (State.Undo, Span(contiguous[0]), contiguous[0]);
        }

        int[][] spread = [.. Enumerable.Range(0, lines.Count).Where(at => lines[at] == added[0]).Select(Spread).OfType<int[]>()];

        if (contiguous.Length == 0 && (spread.Length == 1 || (spread.Length > 0 && spread.Length == twins)))
        {
            return (State.Undo, spread[0], spread[0][0]);
        }

        return contiguous.Length + spread.Length == 0 && !visible.Any(lines.Contains) ? (State.Gone, [], -1) : (State.Displaced, [], -1);
    }

    private static string Decode(byte[] bytes)
    {
        bool bom = bytes is [0xEF, 0xBB, 0xBF, ..];

        return Encoding.UTF8.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0)).ReplaceLineEndings("\n");
    }

    private static IEnumerable<string> GitProblems(string root, List<string> paths)
    {
        if (paths.Count == 0)
        {
            yield break;
        }

        if (Git(root, "rev-parse", "--is-inside-work-tree").Code != 0)
        {
            yield return "This tree is not a git work tree. --apply deletes and edits files that only git restore gives back, so it runs on a committed tree alone.";

            yield break;
        }

        string[] specs = [.. paths.Select(path => ":(literal)" + path)];
        var tracked = Git(root, ["ls-files", "-z", "--", .. specs]).Output.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var changed = Git(root, ["status", "--porcelain=v1", "-z", "--untracked-files=all", "--", .. specs]).Output
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(entry => entry.Length > 3)
            .Select(entry => entry[3..])
            .ToHashSet(StringComparer.Ordinal);
        string prefix = Git(root, "rev-parse", "--show-prefix").Output.Trim();
        string[] unsafePaths = [.. paths.Where(path => !tracked.Contains(path) || changed.Contains(prefix + path)).Order(StringComparer.Ordinal)];

        if (unsafePaths.Length > 0)
        {
            yield return
                $"{unsafePaths.Length} file(s) --apply would delete or edit are not committed as they stand, and only git restore gives back what --apply removes: "
                + "commit them, or restore them, then run this again:\n"
                + string.Join("\n", unsafePaths.Select(path => $"      {path}"));
        }
    }

    internal static (int Code, string Output) Git(string root, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(start)!;
            var errors = process.StandardError.ReadToEndAsync();
            string text = process.StandardOutput.ReadToEnd();

            process.WaitForExit();
            errors.Wait();

            return (process.ExitCode, text);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return (127, string.Empty);
        }
    }

    private static string? Persistence(string root)
    {
        string infrastructure = Path.Combine(root, "Src", "Infrastructure");

        return Directory.Exists(infrastructure)
            ? Directory.EnumerateDirectories(infrastructure, "*.Infrastructure.Persistence").Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')).FirstOrDefault()
            : null;
    }

    private static bool HasInitialMigration(string root) =>
        Persistence(root) is { } persistence
        && Directory.Exists(Path.Combine(root, persistence, "Migrations"))
        && Directory.EnumerateFiles(Path.Combine(root, persistence, "Migrations"), "*ModelSnapshot.cs").Any();

    private static Migration? MigrationOf(string root, string features, Manifest manifest)
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

    private static void SharedDependents(string root, string features, HashSet<string> own, List<Dependent> dependents, List<Hit> uses, List<string> blockers, Func<string, string?[]?> read)
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

            foreach (string file in Walk(root, start, "*.cs"))
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
                        uses.Add(new Hit(Kind.Composition, relative, i + 1, text));
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

    private static Manifest? ReadManifest(string root, string features, List<string> blockers)
    {
        if (FeatureManifest.Read(root, features, out string? problem) is not { } read)
        {
            blockers.Add($"The manifest {problem}.");

            return null;
        }

        if (read.SchemaVersion is not (1 or 2))
        {
            blockers.Add("The manifest has a schema version this command does not read.");

            return null;
        }

        string type = read.Type ?? string.Empty;
        var files = new List<Entry>();
        var shared = new List<string>();
        var consumers = new List<string>();
        var pattern = new Regex($"^(Src|Tests)/(.+/)?Features/{features}/.+$", RegexOptions.None, _timeout);
        var events = read.Files.Keys
            .Where(path => pattern.IsMatch(path) && path.EndsWith("DomainEvent.cs", StringComparison.Ordinal))
            .Select(path => Path.GetFileNameWithoutExtension(path)[..^"DomainEvent".Length])
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (entry, digest) in read.Files)
        {
            bool isShared = _sharedPaths.Any(sharedPath => sharedPath.IsMatch(entry));
            bool isConsumer = _consumer.Match(entry) is { Success: true } consumer && events.Contains(consumer.Groups["base"].Value);

            if (entry.Contains("..", StringComparison.Ordinal) || entry.Contains('\\', StringComparison.Ordinal) || !(pattern.IsMatch(entry) || isShared || isConsumer) || digest.Length != 64)
            {
                blockers.Add(
                    $"The manifest lists '{entry}', which is not a file under a Features/{features} folder of Src or Tests, nor one of the account-deletion files every owner feature shares, "
                    + $"nor the consumer of an event of {features} that new-event wrote in another feature.");

                return null;
            }

            files.Add(new Entry(entry, digest));

            if (isShared)
            {
                shared.Add(entry);
            }
            else if (isConsumer)
            {
                consumers.Add(entry);
            }
        }

        if (type.Length == 0 || files.Count == 0)
        {
            blockers.Add("The manifest names no type or no file.");

            return null;
        }

        if (read.Page is { } page && page != PagePath(features))
        {
            blockers.Add($"The manifest lists the page '{page}', where the generator writes '{PagePath(features)}'.");

            return null;
        }

        string[] outside = [.. read.Edits.Select(edit => edit.Path).Where(path => path.Contains("..", StringComparison.Ordinal) || path.Contains('\\', StringComparison.Ordinal) || Path.IsPathRooted(path)).Distinct(StringComparer.Ordinal)];

        if (outside.Length > 0)
        {
            blockers.Add($"The manifest records an edit in '{outside[0]}', which is not a path inside the tree.");

            return null;
        }

        return new Manifest(type, files, shared, consumers, read.Page, read.Edits, read.AddedFields);
    }

    private static List<string> FeatureFolders(List<string> files, string features)
    {
        string marker = $"/Features/{features}/";

        return [.. files
            .Select(file => file[..(file.IndexOf(marker, StringComparison.Ordinal) + marker.Length - 1)])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    private static List<string> DiscoverFolders(string root, string features)
    {
        var folders = new List<string>();

        foreach (string top in new[] { "Src", "Tests" })
        {
            string start = Path.Combine(root, top);

            if (!Directory.Exists(start))
            {
                continue;
            }

            foreach (string file in Walk(root, start, "*"))
            {
                string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                string marker = $"/Features/{features}/";
                int at = relative.IndexOf(marker, StringComparison.Ordinal);

                if (at >= 0)
                {
                    folders.Add(relative[..(at + marker.Length - 1)]);
                }
            }
        }

        return [.. folders.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }

    private static IEnumerable<string> Walk(string root, string directory, string pattern)
    {
        foreach (string file in Directory.EnumerateFiles(directory, pattern))
        {
            yield return file;
        }

        foreach (string child in Directory.EnumerateDirectories(directory))
        {
            bool tools = Path.GetFileName(child) == "Tools" && Path.GetFullPath(Path.GetDirectoryName(child)!) == Path.GetFullPath(root);

            if (!_skippedFolders.Contains(Path.GetFileName(child)) && !tools)
            {
                foreach (string file in Walk(root, child, pattern))
                {
                    yield return file;
                }
            }
        }
    }

    private static string Kebab(string pascal) => Separated(pascal, '-');

    private static string Separated(string pascal, char separator) =>
        string.Concat(pascal.Select((character, index) =>
            char.IsUpper(character) && index > 0
                ? separator + char.ToLowerInvariant(character).ToString()
                : char.ToLowerInvariant(character).ToString()));

    private static string UpperSnake(string pascal) => Separated(pascal, '_').ToUpperInvariant();

    internal static string PagePath(string features) => $"docs/features/{Kebab(features)}.md";

    private static List<string> DeclaredTypes(string root, Manifest manifest, string type)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in manifest.Files.Where(entry => entry.Path.EndsWith(".cs", StringComparison.Ordinal) && !manifest.Shared.Contains(entry.Path)))
        {
            string full = Path.Combine(root, entry.Path);

            if (!File.Exists(full))
            {
                continue;
            }

            foreach (Match match in _declaration.Matches(File.ReadAllText(full)))
            {
                names.Add(match.Groups[1].Value);
            }
        }

        names.Remove(type);

        return [.. names.Order(StringComparer.Ordinal)];
    }

    private static List<string> Sections(string root, Manifest manifest)
    {
        var sections = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in manifest.Files.Where(entry => entry.Path.EndsWith("Options.cs", StringComparison.Ordinal) && entry.Path.StartsWith("Src/", StringComparison.Ordinal)))
        {
            string full = Path.Combine(root, entry.Path);

            if (File.Exists(full) && _sectionName.Match(File.ReadAllText(full)) is { Success: true } match)
            {
                sections.Add(match.Groups[1].Value);
            }
        }

        return [.. sections.Order(StringComparer.Ordinal)];
    }

    private static string Code(string line)
    {
        string trimmed = line.TrimStart();

        if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("/*", StringComparison.Ordinal) || trimmed.StartsWith('*'))
        {
            return string.Empty;
        }

        var code = new StringBuilder(line.Length);
        int i = 0;

        while (i < line.Length)
        {
            char c = line[i];

            if (c == '/' && i + 1 < line.Length && line[i + 1] == '/')
            {
                break;
            }

            if (c == '\'')
            {
                int close = line.IndexOf('\'', i + (i + 1 < line.Length && line[i + 1] == '\\' ? 3 : 2));

                i = close < 0 ? i + 1 : close + 1;

                continue;
            }

            if (c is '"' or '@' or '$')
            {
                int start = i;
                bool verbatim = false;

                while (i < line.Length && line[i] is '@' or '$')
                {
                    verbatim |= line[i] == '@';
                    i++;
                }

                if (i >= line.Length || line[i] != '"')
                {
                    code.Append(line, start, i - start);

                    continue;
                }

                int quotes = 0;

                while (i + quotes < line.Length && line[i + quotes] == '"')
                {
                    quotes++;
                }

                if (quotes >= 3)
                {
                    int end = line.IndexOf(new string('"', quotes), i + quotes, StringComparison.Ordinal);

                    i = end < 0 ? line.Length : end + quotes;
                }
                else
                {
                    i++;

                    while (i < line.Length)
                    {
                        if (!verbatim && line[i] == '\\')
                        {
                            i += 2;
                        }
                        else if (verbatim && line[i] == '"' && i + 1 < line.Length && line[i + 1] == '"')
                        {
                            i += 2;
                        }
                        else if (line[i] == '"')
                        {
                            i++;

                            break;
                        }
                        else
                        {
                            i++;
                        }
                    }
                }

                code.Append("\"\"");

                continue;
            }

            code.Append(c);
            i++;
        }

        return code.ToString();
    }

    private static bool Outside(string relative, List<string> folders, HashSet<string> own) =>
        !own.Contains(relative) && !folders.Any(folder => relative.StartsWith(folder + "/", StringComparison.Ordinal));

    private static List<Hit> ScanCode(string root, string features, string? type, List<string> declared, List<string> folders, HashSet<string> own, Func<string, string?[]?> read)
    {
        var composition = new List<Regex>
        {
            Rx($@"\bAdd{features}(Feature)?\b"),
            Rx($@"nameof\(\w+\.Add{features}\)"),
        };

        var named = Rx($@"AddFeature\(\s*""{features}""");
        var model = new List<Regex> { Rx($@"\b{features}Schema\b") };
        var reference = new List<Regex> { Rx($@"\.Features\.{features}\b") };
        Regex? typedRegistration = null;
        Regex? declaredName = null;

        if (type is not null)
        {
            model.Add(Rx($@"\b{type}Record(Configuration)?\b"));

            if (declared.Count > 0)
            {
                string names = string.Join("|", declared.Select(Regex.Escape));

                declaredName = Rx($@"\b(?:{names})\b");
                typedRegistration = Rx($@"\b(Try)?Add\w*\s*[<(].*\b(?:{names})\b");
            }
        }

        var hits = new List<Hit>();

        foreach (string top in new[] { "Src", "Tests" })
        {
            string start = Path.Combine(root, top);

            if (!Directory.Exists(start))
            {
                continue;
            }

            foreach (string file in Walk(root, start, "*.cs"))
            {
                string relative = Path.GetRelativePath(root, file).Replace('\\', '/');

                if (!Outside(relative, folders, own) || read(relative) is not { } lines)
                {
                    continue;
                }

                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i] is not { } line)
                    {
                        continue;
                    }

                    string code = Code(line);

                    Kind? kind = composition.Any(pattern => pattern.IsMatch(code)) || named.IsMatch(line) || (typedRegistration?.IsMatch(code) ?? false) ? Kind.Composition
                        : model.Any(pattern => pattern.IsMatch(code)) ? Kind.Model
                        : reference.Any(pattern => pattern.IsMatch(code)) || (declaredName?.IsMatch(code) ?? false) ? Kind.Reference
                        : null;

                    if (kind is not null)
                    {
                        hits.Add(new Hit(kind.Value, relative, i + 1, line.Trim()));
                    }
                }
            }
        }

        return hits;
    }

    private static List<Note> ScanNotes(string root, string features, string type, List<string> sections, List<string> folders, HashSet<string> own, Func<string, string?[]?> read)
    {
        const string Configuration = "Configuration";
        const string Documentation = "Documentation and samples";

        string kebab = Kebab(features);
        var route = Rx($@"/api/v1/{Regex.Escape(kebab)}(?![\w-])");
        var link = Rx($@"\]\([^)\s]*\bfeatures/{Regex.Escape(kebab)}\.md(#[^)\s]*)?\)");
        var code = Rx($@"`{Regex.Escape(char.ToLowerInvariant(type[0]) + type[1..])}\.[A-Za-z]+`");
        var keys = sections.Select(section => Rx($@"^\s*""{section}""\s*:")).ToList();
        var variables = sections.Select(section => Rx($@"^\s*#?\s*{UpperSnake(section)}_[A-Z0-9_]*=")).ToList();
        var mapped = sections.Select(section => Rx($@"\b{section}__")).ToList();
        var headings = sections.Select(section => $"### `{section}`").ToHashSet(StringComparer.Ordinal);
        var notes = new List<Note>();

        foreach (string file in Walk(root, root, "*"))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            string name = Path.GetFileName(relative);
            bool settings = name.StartsWith("appsettings", StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal);
            bool environment = name.StartsWith(".env", StringComparison.Ordinal);
            bool compose = (name.StartsWith("docker-compose", StringComparison.Ordinal) || name.StartsWith("compose", StringComparison.Ordinal))
                && (name.EndsWith(".yml", StringComparison.Ordinal) || name.EndsWith(".yaml", StringComparison.Ordinal));
            bool markdown = name.EndsWith(".md", StringComparison.Ordinal);
            bool samples = name.EndsWith(".http", StringComparison.Ordinal);

            if (!(settings || environment || compose || markdown || samples) || !Outside(relative, folders, own) || read(relative) is not { } lines)
            {
                continue;
            }

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i] is not { } line)
                {
                    continue;
                }

                string? group =
                    settings && keys.Any(key => key.IsMatch(line)) ? Configuration
                    : environment && variables.Any(variable => variable.IsMatch(line)) ? Configuration
                    : compose && mapped.Any(key => key.IsMatch(line)) ? Configuration
                    : markdown && headings.Contains(line.TrimEnd()) ? Configuration
                    : markdown && (link.IsMatch(line) || route.IsMatch(line) || code.IsMatch(line)) ? Documentation
                    : samples && route.IsMatch(line) ? Documentation
                    : null;

                if (group is not null)
                {
                    notes.Add(new Note(group, relative, i + 1, line.Trim()));
                }
            }
        }

        return notes;
    }

    private static Regex Rx(string pattern) => new(pattern, RegexOptions.None, _timeout);

    private static void PrintReport(TextWriter output, string root, Analysis analysis)
    {
        output.WriteLine($"Feature {analysis.Features}" + (analysis.Type is null ? " (no manifest, so its type is unknown)" : $" (type {analysis.Type}), from {FeatureManifest.PathOf(analysis.Features)}"));

        if (!analysis.HasManifest)
        {
            output.WriteLine();
            output.WriteLine($"Feature folders found ({analysis.Folders.Count}), none of them recorded by a manifest:");

            foreach (string folder in analysis.Folders)
            {
                output.WriteLine($"  {folder}");
            }

            output.WriteLine();
            PrintHits(output, analysis);
            PrintBlockers(output, analysis);

            return;
        }

        var unchanged = analysis.Owned.Except(analysis.Modified, StringComparer.Ordinal).Except(analysis.Missing, StringComparer.Ordinal).ToList();
        var undone = analysis.Undos.Where(undo => undo.State == State.Undo).ToList();

        output.WriteLine();
        output.WriteLine($"--apply deletes the {unchanged.Count} file(s) the generators wrote, unchanged since, in:");

        foreach (string folder in analysis.Folders)
        {
            output.WriteLine($"  {folder}");
        }

        foreach (string file in unchanged.Where(file => !analysis.Folders.Any(folder => file.StartsWith(folder + "/", StringComparison.Ordinal))))
        {
            output.WriteLine($"  {file}");
        }

        output.WriteLine();
        output.WriteLine($"--apply undoes the {undone.Count} edit(s) the generators recorded, each exactly as it was made:");

        foreach (var undo in undone)
        {
            output.WriteLine($"  {undo.Edit.Path}: {Count(undo.Edit.Lines.Length, "line")} {(undo.Edit.Replaced.Length > 0 ? $"in place of {Count(undo.Edit.Replaced.Length, "line")} " : string.Empty)}{undo.Edit.Position} '{undo.Edit.Anchor.Trim()}'");
        }

        if (analysis.AddedFields.Count > 0)
        {
            output.WriteLine();
            output.WriteLine("Fields new-field added, which go with the files they changed:");

            foreach (var (declaration, added, paths) in analysis.AddedFields)
            {
                output.WriteLine($"  {declaration}, in {Count(paths.Length, "file")}, with its migration {added}");
            }
        }

        if (analysis.Page is not null)
        {
            output.WriteLine();
            output.WriteLine(analysis.PageGone
                ? $"Feature page: {analysis.Page} is already gone."
                : $"Feature page: {analysis.Page}, written by the generator; --apply deletes it, edited or not.");
        }

        if (analysis.Modified.Count + analysis.Extra.Count > 0)
        {
            output.WriteLine();
            output.WriteLine("--apply deletes these too, though they changed since the generators wrote them; git log and git show keep what they held:");

            foreach (string file in analysis.Modified)
            {
                output.WriteLine($"  Edited: {file}");
            }

            foreach (string file in analysis.Extra)
            {
                output.WriteLine($"  Added: {file}");
            }
        }

        var gone = analysis.Undos.Where(undo => undo.State == State.Gone).ToList();

        if (analysis.Missing.Count + gone.Count > 0)
        {
            output.WriteLine();
            output.WriteLine("Already gone, so left as they are:");

            foreach (string file in analysis.Missing)
            {
                output.WriteLine($"  Missing: {file}");
            }

            foreach (var undo in gone)
            {
                output.WriteLine($"  Undone: {undo.Edit.Path}: {Count(undo.Edit.Lines.Length, "line")} {undo.Edit.Position} '{undo.Edit.Anchor.Trim()}'");
            }
        }

        if (analysis.Shared.Count > 0)
        {
            output.WriteLine();
            output.WriteLine($"Shared by every owner feature (brought by this one into a tree generated with none): {analysis.Shared.Count} file(s)");

            foreach (string file in analysis.Shared)
            {
                output.WriteLine($"  {file}");
            }

            output.WriteLine(analysis.Dependents.Count + analysis.SharedUses.Count == 0 ? "Still used by: nothing." : "Still used by:");

            foreach (var dependent in analysis.Dependents)
            {
                output.WriteLine($"  {dependent.Name} ({dependent.How})");
            }

            foreach (var use in analysis.SharedUses)
            {
                output.WriteLine($"  {use.Path}:{use.Line}  {use.Text}");
            }
        }

        output.WriteLine();
        output.WriteLine("Then --apply runs, from one build:");

        foreach (var step in Steps(root, analysis).Concat(ContractSteps(root)))
        {
            output.WriteLine($"  {step.Display}");
        }

        if (analysis.Migration is { } migration)
        {
            output.WriteLine(
                $"  {migration.Name} drops the tables of {analysis.Features}"
                + (migration.Schema is null ? string.Empty : $" and its schema \"{migration.Schema}\"")
                + $"; {string.Join(", ", [$"Add{analysis.Features}", .. analysis.AddedFields.Select(added => added.Migration)])}, which a database may have applied, "
                + (analysis.AddedFields.Count == 0 ? "stays as it is." : "stay as they are."));
        }

        output.WriteLine();
        PrintHits(output, analysis);
        PrintNotes(output, analysis);
        PrintBlockers(output, analysis);
    }

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    private static void PrintHits(TextWriter output, Analysis analysis)
    {
        output.WriteLine(analysis.Hits.Count == 0
            ? "Code outside the feature that still uses it, recorded nowhere: none."
            : $"Code outside the feature that still uses it, recorded nowhere, which --apply refuses while it is there: {analysis.Hits.Count} line(s)");

        foreach (var hit in analysis.Hits)
        {
            output.WriteLine($"  {hit.Path}:{hit.Line}  {hit.Text}");
        }
    }

    private static void PrintNotes(TextWriter output, Analysis analysis)
    {
        foreach (string group in new[] { "Configuration", "Documentation and samples" })
        {
            var notes = analysis.Notes.Where(note => note.Group == group).ToList();

            output.WriteLine(notes.Count == 0
                ? $"{group} outside the feature that still names it, recorded nowhere: none."
                : $"{group} outside the feature that still names it, recorded nowhere, left to you: {notes.Count} line(s)");

            foreach (var note in notes)
            {
                output.WriteLine($"  {note.Path}:{note.Line}  {note.Text}");
            }
        }
    }

    private static void PrintBlockers(TextWriter output, Analysis analysis)
    {
        output.WriteLine();
        output.WriteLine(analysis.Blockers.Count == 0 ? "Blockers: none." : "Blockers:");

        foreach (string blocker in analysis.Blockers)
        {
            output.WriteLine($"  - {blocker}");
        }
    }

    private static List<TextEdits.Command> Steps(string root, Analysis analysis) =>
    [
        TextEdits.Task(root, "docs-index"),
        TextEdits.Task(root, "build"),
        .. analysis.Migration is { } migration ? [TextEdits.FromLastBuild(root, "migration-add", migration.Name)] : Array.Empty<TextEdits.Command>(),
    ];

    private static List<TextEdits.Command> ContractSteps(string root)
    {
        string presentation = Path.Combine(root, "Src", "Presentation");

        if (!Directory.Exists(presentation) || !Directory.EnumerateDirectories(presentation, "*.Api").Any())
        {
            return [];
        }

        string client = Directory.EnumerateDirectories(presentation, "*.Presentation.Client").Select(Path.GetFileName).OfType<string>().FirstOrDefault() ?? string.Empty;

        return TextEdits.ContractSteps(root, client, built: true);
    }

    private static int Remove(string root, Analysis analysis, TextWriter output, TextWriter error, Func<TextEdits.Command, int> run)
    {
        string[] deleted = [.. analysis.Deleted];

        try
        {
            TextEdits.Commit(root, [], [.. analysis.Undone.Select(pair => (pair.Key, pair.Value.Original, pair.Value.After))], deleted);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"error: nothing was left behind, but the removal failed: {exception.Message}");

            return 1;
        }

        foreach (string folder in analysis.Folders)
        {
            RemoveEmpty(Path.Combine(root, folder));
            RemoveEmptyAncestors(root, folder);
        }

        foreach (string file in deleted)
        {
            RemoveEmptyAncestors(root, file);
        }

        output.WriteLine(
            $"Removed {analysis.Features}: {Count(deleted.Length, "file")} deleted, its manifest and page among them, "
            + $"{Count(analysis.Undos.Count(undo => undo.State == State.Undo), "edit")} undone in {Count(analysis.Undone.Count, "file")}, and the folders left empty.");

        if (!TextEdits.RunAll(Steps(root, analysis), run, output, error, $"{analysis.Features} removed, its edits undone"))
        {
            foreach (var step in ContractSteps(root))
            {
                error.WriteLine($"  {step.Display}");
            }

            return 1;
        }

        if (analysis.Migration is { Schema: { } schema } migration && !DropSchema(root, migration, schema))
        {
            output.WriteLine($"{migration.Name} was not found to add DropSchema(\"{schema}\") to its Up and EnsureSchema(\"{schema}\") to its Down: add them by hand.");
        }

        var contract = ContractSteps(root);

        if (contract.Count > 0 && !TextEdits.RunAll(contract, run, output, error, $"{analysis.Features} removed and its migration added"))
        {
            return 1;
        }

        if (analysis.Notes.Count > 0)
        {
            output.WriteLine();
            PrintNotes(output, analysis);
        }

        return 0;
    }

    private static bool DropSchema(string root, Migration migration, string schema)
    {
        string folder = Path.Combine(root, migration.Persistence, "Migrations");
        string? file = Directory.EnumerateFiles(folder, $"*_{migration.Name}.cs").Order(StringComparer.Ordinal).LastOrDefault();

        if (file is null)
        {
            return false;
        }

        byte[] bytes = File.ReadAllBytes(file);
        bool bom = bytes is [0xEF, 0xBB, 0xBF, ..];
        string text = Decode(bytes);
        const string Up = "protected override void Up(MigrationBuilder migrationBuilder)\n        {\n";
        const string Down = "protected override void Down(MigrationBuilder migrationBuilder)\n        {\n";
        int up = text.IndexOf(Up, StringComparison.Ordinal);
        int upEnd = up < 0 ? -1 : text.IndexOf("\n        }\n", up, StringComparison.Ordinal);

        if (upEnd < 0 || text.IndexOf(Down, StringComparison.Ordinal) < 0)
        {
            return false;
        }

        string ensure = $"            migrationBuilder.EnsureSchema(\n                name: \"{schema}\");\n";
        int down = text.IndexOf(Down, StringComparison.Ordinal) + Down.Length;

        if (!text[down..].Contains(ensure, StringComparison.Ordinal))
        {
            text = text.Insert(down, ensure + "\n");
        }

        text = text.Insert(upEnd, $"\n\n            migrationBuilder.DropSchema(\n                name: \"{schema}\");");
        File.WriteAllBytes(file, [.. bom ? Encoding.UTF8.Preamble : [], .. Encoding.UTF8.GetBytes(text)]);

        return true;
    }

    private static void RemoveEmpty(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (string child in Directory.EnumerateDirectories(directory))
        {
            RemoveEmpty(child);
        }

        if (!Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
        }
    }

    private static void RemoveEmptyAncestors(string root, string relative)
    {
        string? folder = Path.GetDirectoryName(relative)?.Replace('\\', '/');

        while (!string.IsNullOrEmpty(folder) && folder is not ("Src" or "Tests" or "docs"))
        {
            string full = Path.Combine(root, folder);

            if (Directory.Exists(full))
            {
                if (Directory.EnumerateFileSystemEntries(full).Any())
                {
                    return;
                }

                Directory.Delete(full);
            }

            folder = Path.GetDirectoryName(folder)?.Replace('\\', '/');
        }
    }
}

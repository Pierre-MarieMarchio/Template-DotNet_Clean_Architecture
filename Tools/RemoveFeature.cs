#:property UseAppHost=false

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

internal static class RemoveFeature
{
    private const string _usage =
        "Usage: dotnet run Tools/RemoveFeature.cs <Features> [--apply] [--root <path>]\n"
        + "  <Features>  the folder of the feature, PascalCase and plural, e.g. Widgets\n"
        + "  --apply     remove the feature its manifest records, edited or not: delete its files and its page,\n"
        + "              undo each edit the generators recorded, then build, add the Remove<Features> migration\n"
        + "              and rewrite the OpenAPI reference and what is written from it. It refuses, writing\n"
        + "              nothing, while a file it would delete or change is not committed, a model change waits\n"
        + "              for a migration, or code outside the feature still uses it. Without --apply, the\n"
        + "              command only reports and writes nothing.\n"
        + "  --root      the tree to read, default the current directory.\n"
        + "       dotnet run Tools/RemoveFeature.cs --self-test";

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

    private static int Main(string[] args)
    {
        if (Array.IndexOf(args, "--self-test") >= 0)
        {
            return SelfTest();
        }

        if (args is ["--help"] or ["-h"])
        {
            Console.WriteLine(_usage);

            return 0;
        }

        return Run(args, Console.Out, Console.Error, TextEdits.Execute);
    }

    private static int Run(string[] args, TextWriter output, TextWriter error, Func<TextEdits.Command, int> run)
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

    private static (int Code, string Output) Git(string root, params string[] arguments)
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

    private static string PagePath(string features) => $"docs/features/{Kebab(features)}.md";

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
        TextEdits.Task(root, "build"),
        .. analysis.Migration is { } migration ? [TextEdits.FromLastBuild(root, "migration-add", migration.Name)] : Array.Empty<TextEdits.Command>(),
    ];

    private static List<TextEdits.Command> ContractSteps(string root)
    {
        string presentation = Path.Combine(root, "Src", "Presentation");
        string clients = Path.Combine(root, "Src", "Clients");

        if (!Directory.Exists(presentation) || !Directory.EnumerateDirectories(presentation, "*.Api").Any())
        {
            return [];
        }

        string client = Directory.Exists(clients) ? Directory.EnumerateDirectories(clients).Select(Path.GetFileName).OfType<string>().FirstOrDefault() ?? string.Empty : string.Empty;

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

    private static int SelfTest()
    {
        string scratch = Path.Combine(Path.GetTempPath(), "removefeature-selftest-" + Guid.CreateVersion7().ToString("N"));
        var problems = new List<string>();
        int checks = 0;

        void Expect(bool condition, string message)
        {
            checks++;

            if (!condition)
            {
                problems.Add(message);
            }
        }

        try
        {
            Directory.CreateDirectory(scratch);

            HandWrittenCases(scratch, Expect);
            ReportCases(scratch, Expect);
            UndoCases(scratch, Expect);
            GeneratorCases(scratch, Expect);
            MigrationCases(scratch, Expect);
            OrderCases(scratch, Expect);
            NameCollisionCases(scratch, Expect);
            SharedFilesCases(scratch, Expect);
            PageCases(scratch, Expect);
            WithoutTheApiCases(scratch, Expect);
        }
        finally
        {
            if (Directory.Exists(scratch))
            {
                DeleteTreeWithReadOnlyFiles(scratch);
            }
        }

        Console.WriteLine($"Self-test: {checks} check(s), {problems.Count} problem(s).");

        foreach (string problem in problems)
        {
            Console.Error.WriteLine($"  {problem}");
        }

        return problems.Count == 0 ? 0 : 1;
    }

    private static (int Code, string Output, string Error) Invoke(string root, params string[] args) => Invoke(root, Recording([]), args);

    private static (int Code, string Output, string Error) Invoke(string root, Func<TextEdits.Command, int> run, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int code = Run([.. args, "--root", root], output, error, run);

        return (code, output.ToString(), error.ToString());
    }

    private const string _removeMigration =
        "namespace Acme.Infrastructure.Persistence.Migrations\n{\n    public partial class {0} : Migration\n    {\n"
        + "        protected override void Up(MigrationBuilder migrationBuilder)\n        {\n            migrationBuilder.DropTable(\n                name: \"widgets\",\n                schema: \"widgets\");\n        }\n\n"
        + "        protected override void Down(MigrationBuilder migrationBuilder)\n        {\n            migrationBuilder.CreateTable(\n                name: \"widgets\",\n                schema: \"widgets\");\n        }\n    }\n}\n";

    private static Func<TextEdits.Command, int> Recording(List<TextEdits.Command> ran, string? failing = null, bool ensured = false) => command =>
    {
        ran.Add(command);

        int at = Array.IndexOf(command.Arguments, "migration-add");

        if (at >= 0 && command.Display != failing)
        {
            string name = command.Arguments[at + 1];

            string migration = _removeMigration.Replace("{0}", name, StringComparison.Ordinal);

            Put(
                command.WorkingDirectory,
                $"Src/Infrastructure/A.Infrastructure.Persistence/Migrations/20990101000000_{name}.cs",
                ensured ? migration.Replace("        {\n            migrationBuilder.CreateTable(", "        {\n            migrationBuilder.EnsureSchema(\n                name: \"widgets\");\n\n            migrationBuilder.CreateTable(", StringComparison.Ordinal) : migration);
        }

        return command.Display == failing ? 1 : 0;
    };

    private static string[] Launched(List<TextEdits.Command> ran) => [.. ran.Select(command => command.Display)];

    private static void Commit(string root)
    {
        if (!Directory.Exists(Path.Combine(root, ".git")))
        {
            Git(root, "init", "--quiet");
        }

        Git(root, "add", "-A");
        Git(root, "-c", "user.name=self-test", "-c", "user.email=self-test@localhost", "-c", "commit.gpgsign=false", "commit", "--quiet", "--allow-empty", "-m", "self-test");
    }

    private static Dictionary<string, string> Snapshot(string root) =>
        Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Select(path => (Path: path, Relative: Path.GetRelativePath(root, path).Replace('\\', '/')))
            .Where(entry => entry.Relative != ".git" && !entry.Relative.StartsWith(".git/", StringComparison.Ordinal))
            .ToDictionary(
                entry => entry.Relative,
                entry => File.Exists(entry.Path) ? Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(entry.Path))) : "<directory>",
                StringComparer.Ordinal);

    private static bool Same(Dictionary<string, string> before, Dictionary<string, string> after) =>
        before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out string? hash) && hash == pair.Value);

    private static string Differences(Dictionary<string, string> before, Dictionary<string, string> after) =>
        string.Join(", ", before.Keys.Union(after.Keys).Where(key => !before.TryGetValue(key, out string? hash) || !after.TryGetValue(key, out string? other) || hash != other).Order(StringComparer.Ordinal).Take(8));

    private static void Put(string root, string relative, string content)
    {
        string full = Path.Combine(root, relative);

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private static string Sha(string root, string relative) =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, relative))));

    private static readonly string[] _skeletonFiles =
    [
        "Src/Domain/A.Domain/Features/Widgets/Model/Entities/Widget.cs",
        "Src/Application/A.Application/Features/Widgets/Services/WidgetService.cs",
        "Src/Application/A.Application/Features/Widgets/UseCases/Queries/GetWidget/GetWidget.cs",
        "Src/Application/A.Application/Features/Widgets/Configuration/Options/WidgetLimitsOptions.cs",
        "Tests/Application/A.Application.UnitTests/Features/Widgets/WidgetServiceTests.cs",
    ];

    private static string Declarations(string name) => name switch
    {
        "Widget" => "public sealed record Widget;\n",
        "WidgetService" => "public interface IWidgetService;\npublic interface IWidgetStore;\npublic sealed class WidgetService;\npublic sealed class WidgetStore;\n",
        "GetWidget" => "public sealed record GetWidgetQuery;\npublic sealed class GetWidgetQueryHandler;\n",
        "WidgetLimitsOptions" => "public sealed class WidgetLimitsOptions\n{\n    public const string SectionName = \"WidgetLimits\";\n}\n",
        _ => "public sealed class WidgetServiceTests;\n",
    };

    private const string _program = "builder.Services.AddTodoLists();\n\nbuilder.Services.AddCacheStore();\n";

    private static void DeleteTreeWithReadOnlyFiles(string path)
    {
        foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(path, recursive: true);
    }

    private static string Skeleton(string scratch, string edits = "")
    {
        string root = Path.Combine(scratch, Guid.CreateVersion7().ToString("N"));

        foreach (string file in _skeletonFiles)
        {
            Put(root, file, "namespace Acme;\n// " + file + "\n" + Declarations(Path.GetFileNameWithoutExtension(file)));
        }

        Put(root, "Src/Presentation/A.Api/Program.cs", _program);
        Put(root, "Src/Infrastructure/A.Infrastructure.Persistence/Common/Contexts/AppDbContext.cs", "public const string TodoListsSchema = \"todo_lists\";\n");
        Put(root, "Src/Infrastructure/A.Infrastructure.Persistence/Migrations/20240101_InitialCreate.cs", "schema: \"todo_lists\"\n");
        Put(root, "README.md", "A template.\n");

        var json = new StringBuilder($"{{\n  \"schemaVersion\": {(edits.Length > 0 ? 2 : 1)},\n  \"generator\": \"Tools/NewFeature.cs\",\n  \"feature\": \"Widgets\",\n  \"type\": \"Widget\",\n  \"files\": [\n");

        json.Append(string.Join(",\n", _skeletonFiles.Order(StringComparer.Ordinal).Select(file => $"    {{ \"path\": \"{file}\", \"sha256\": \"{Sha(root, file)}\" }}")));
        json.Append(edits.Length > 0 ? $"\n  ],\n  \"edits\": [{edits}]\n}}\n" : "\n  ]\n}\n");
        Put(root, ".feature-manifest/Widgets.json", json.ToString());
        Commit(root);

        return root;
    }

    private static void Refused(Action<bool, string> expect, string name, string tree, string features, params string[] expected)
    {
        var snapshot = Snapshot(tree);
        var ran = new List<TextEdits.Command>();
        var result = Invoke(tree, Recording(ran), features, "--apply");

        expect(result.Code == 1, $"{name}: --apply did not refuse: {result.Output}");
        expect(Same(snapshot, Snapshot(tree)), $"{name}: a refusal changed the tree: {Differences(snapshot, Snapshot(tree))}");
        expect(ran.All(command => command.Display.EndsWith("migration-check", StringComparison.Ordinal)), $"{name}: a refusal ran {string.Join(", ", Launched(ran))}.");

        foreach (string text in expected)
        {
            expect((result.Output + result.Error).Contains(text, StringComparison.Ordinal), $"{name}: the report does not say '{text}': {result.Output}{result.Error}");
        }
    }

    private static void HandWrittenCases(string scratch, Action<bool, string> expect)
    {
        string root = Skeleton(scratch);
        var before = Snapshot(root);
        var report = Invoke(root, "Widgets");

        expect(report.Code == 0 && Same(before, Snapshot(root)), "The report changed the tree, or failed on a clean skeleton.");
        expect(report.Output.Contains("Blockers: none.", StringComparison.Ordinal), $"The report named a blocker on a clean skeleton: {report.Output}");

        var ran = new List<TextEdits.Command>();
        var applied = Invoke(root, Recording(ran), "Widgets", "--apply");
        var after = Snapshot(root);

        expect(applied.Code == 0, $"An untouched skeleton was refused: {applied.Error}{applied.Output}");
        expect(_skeletonFiles.All(file => !File.Exists(Path.Combine(root, file))), "A generated file was left behind.");
        expect(!Directory.Exists(Path.Combine(root, FeatureManifest.Folder)), "The manifest or its folder was left behind.");
        expect(!after.Keys.Any(key => key.Contains("Widgets", StringComparison.Ordinal)), "A folder of the feature was left behind.");
        expect(File.Exists(Path.Combine(root, "README.md")) && File.Exists(Path.Combine(root, "Src/Presentation/A.Api/Program.cs")), "A file the feature did not own was deleted.");
        expect(
            Launched(ran).SequenceEqual(["dotnet run Tools/Tasks.cs build", "dotnet run Tools/Tasks.cs -- openapi --no-build"]),
            $"A feature without a migration did not build once and rewrite the OpenAPI reference from that build: {string.Join(" | ", Launched(ran))}");

        var again = Invoke(root, "Widgets", "--apply");

        expect(again.Code == 0 && again.Output.Contains("No feature folder", StringComparison.Ordinal), "A second --apply did not say that nothing is left.");

        void Removed(string name, Action<string> change, string listed)
        {
            string tree = Skeleton(scratch);
            var original = Snapshot(tree);

            change(tree);
            Commit(tree);

            var result = Invoke(tree, "Widgets");

            expect(result.Output.Contains(listed, StringComparison.Ordinal), $"{name}: the report does not list '{listed}': {result.Output}");
            expect(result.Output.Contains("Blockers: none.", StringComparison.Ordinal), $"{name}: the report took it for a blocker: {result.Output}");

            var removed = Invoke(tree, "Widgets", "--apply");
            var left = Snapshot(tree);

            expect(removed.Code == 0, $"{name}: --apply refused a feature whose files changed since: {removed.Output}{removed.Error}");
            expect(
                left.Keys.All(key => original.ContainsKey(key)) && !left.Keys.Any(key => key.Contains("/Features/Widgets", StringComparison.Ordinal) || key.StartsWith(FeatureManifest.Folder, StringComparison.Ordinal)),
                $"{name}: --apply left a file of the feature: {Differences(original, left)}");
        }

        Removed("edited file", tree => File.AppendAllText(Path.Combine(tree, _skeletonFiles[1]), "// edited\n"), "Edited: " + _skeletonFiles[1]);
        Removed("added file", tree => Put(tree, "Src/Domain/A.Domain/Features/Widgets/Model/Entities/Extra.cs", "x\n"), "Added: Src/Domain/A.Domain/Features/Widgets/Model/Entities/Extra.cs");
        Removed("added file in a new subfolder", tree => Put(tree, "Tests/Application/A.Application.UnitTests/Features/Widgets/Sub/Extra.cs", "x\n"), "Added: Tests/Application/A.Application.UnitTests/Features/Widgets/Sub/Extra.cs");
        Removed("missing file", tree => File.Delete(Path.Combine(tree, _skeletonFiles[0])), "Missing: " + _skeletonFiles[0]);

        string dirty = Skeleton(scratch);

        File.AppendAllText(Path.Combine(dirty, _skeletonFiles[1]), "// not committed\n");
        Refused(expect, "an edit not committed", dirty, "Widgets", "not committed as they stand", _skeletonFiles[1]);

        string untracked = Skeleton(scratch);

        Put(untracked, "Src/Domain/A.Domain/Features/Widgets/Model/Entities/Draft.cs", "x\n");
        Refused(expect, "a file git does not track", untracked, "Widgets", "not committed as they stand", "Draft.cs");

        string loose = Skeleton(scratch);

        DeleteTreeWithReadOnlyFiles(Path.Combine(loose, ".git"));
        Refused(expect, "a tree git does not hold", loose, "Widgets", "not a git work tree");

        void Composed(string name, string path, string line, params string[] expected)
        {
            string tree = Skeleton(scratch);

            Put(tree, path, line + "\n");
            Commit(tree);
            Refused(expect, name, tree, "Widgets", ["still use its types", $"{path}:1", .. expected]);
        }

        Composed("an outside test uses the namespace", "Tests/Other/A.Other.Tests/UsesIt.cs", "using Acme.Application.Features.Widgets.Services;");
        Composed("another feature injects a type", "Src/Application/A.Application/Features/Reminders/Services/ReminderService.cs", "public sealed class ReminderService(IWidgetService widgets);", "in Reminders, which depend(s) on it");
        Composed("a registration by hand", "Src/Presentation/A.Api/Wiring.cs", "builder.Services.AddWidgets();");
        Composed("a set by hand", "Src/Infrastructure/A.Infrastructure.Persistence/Common/Contexts/Sets.cs", "internal DbSet<WidgetRecord> Widgets => Set<WidgetRecord>();");
        Composed("a named feature by hand", "Src/Application/A.Application/Named.cs", "services.AddFeature(\"Widgets\");");

        string manifestPath = $"{FeatureManifest.Folder}/Widgets.json";

        void Rewritten(string name, Func<string, string> change, string expected)
        {
            string tree = Skeleton(scratch);

            Put(tree, manifestPath, change(File.ReadAllText(Path.Combine(tree, manifestPath))));
            Commit(tree);
            Refused(expect, name, tree, "Widgets", expected);
        }

        Rewritten("a schema this command does not read", text => text.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 3", StringComparison.Ordinal), "a schema version this command does not read");
        Rewritten("a path leaving the feature", text => text.Replace(_skeletonFiles[0], "Src/Domain/A.Domain/Features/Widgets/../../../../README.md", StringComparison.Ordinal), "not a file under a Features/Widgets folder");
        Rewritten("another feature's manifest", text => text.Replace("\"feature\": \"Widgets\"", "\"feature\": \"Gadgets\"", StringComparison.Ordinal), "does not read as the manifest of Widgets");
        Rewritten("a consumer of an event the feature does not hold", text => text.Replace(_skeletonFiles[2], "Src/Application/A.Application/Features/Orders/Services/Consumers/WidgetPaid/ShipOnWidgetPaidConsumer.cs", StringComparison.Ordinal), "nor the consumer of an event of Widgets");

        string bare = Skeleton(scratch);

        File.Delete(Path.Combine(bare, manifestPath));
        Commit(bare);

        var bareReport = Invoke(bare, "Widgets");

        expect(bareReport.Code == 0 && bareReport.Output.Contains("no manifest", StringComparison.Ordinal), "A feature without a manifest was not reported as such.");
        Refused(expect, "a feature without a manifest", bare, "Widgets", "Only a manifest says");

        string checkedTree = Skeleton(scratch);

        Put(checkedTree, "Src/Infrastructure/A.Infrastructure.Persistence/Migrations/AppDbContextModelSnapshot.cs", "// the initial migration's snapshot\n");
        Commit(checkedTree);

        var pending = Snapshot(checkedTree);
        var checks = new List<TextEdits.Command>();
        var waiting = Invoke(checkedTree, Recording(checks, "dotnet run Tools/Tasks.cs migration-check"), "Widgets", "--apply");

        expect(waiting.Code == 1 && Same(pending, Snapshot(checkedTree)), "A model change waiting for a migration did not stop --apply before it changed anything.");
        expect(waiting.Error.Contains("migration-check` failed", StringComparison.Ordinal) && Launched(checks).SequenceEqual(["dotnet run Tools/Tasks.cs migration-check"]), $"The refusal for a waiting model change does not say why, or ran more: {waiting.Error}");

        var auth = Invoke(Skeleton(scratch), "Auth", "--apply");

        expect(!auth.Output.Contains("Removed", StringComparison.Ordinal), "Auth was removed.");

        var badName = Invoke(Skeleton(scratch), "widgets", "--apply");

        expect(badName.Code == 1, "A name that is not PascalCase was accepted.");
    }

    private static void ReportCases(string scratch, Action<bool, string> expect)
    {
        string root = Skeleton(scratch);
        (string Path, string Text, bool Listed)[] lines =
        [
            ("Src/Presentation/A.Api/appsettings.Development.json", "  \"WidgetLimits\": {", true),
            ("Src/Presentation/A.Api/appsettings.json", "  \"WidgetLimitsArchive\": 1,", false),
            (".env.example", "WIDGET_LIMITS_MAX_ITEMS=3", true),
            (".env.example", "OTHER_WIDGET_LIMITS_MAX=3", false),
            ("docker-compose.yml", "  WidgetLimits__MaxItems: 3", true),
            ("docs/reference/configuration.md", "### `WidgetLimits`", true),
            ("docs/reference/configuration.md", "The Widgets feature bounds what a widget holds.", false),
            //#if (api)
            ("docs/reference/api.md", "Under `/api/v1/widgets`.", true),
            ("docs/reference/api.md", "| `widget.notFound` | 404 | Missing, or another caller's |", true),
            ("docs/reference/api.md", "Under `/api/v1/widgets-archive`, which Widgets feeds.", false),
            //#endif
            ("docs/guides/adding-a-feature.md", "Read [the Widgets page](../features/widgets.md#fields) first.", true),
            ("docs/guides/adding-a-feature.md", "Widgets, WidgetService and IWidgetStore are words in a sentence.", false),
            ("A.Api.http", "GET {{host}}/api/v1/widgets/{{widgetId}}", true),
            ("A.Api.http", "GET {{host}}/api/v1/widgets-archive", false),
            ("A.sln", "Project(\"Widgets\") = \"Widgets\"", false),
            ("Directory.Build.props", "<Widgets>true</Widgets>", false),
            ("Directory.Build.targets", "<WidgetService />", false),
            ("Tools/Sample.cs", "var service = new WidgetService();", false),
            ("Src/Clients/A.Client/Generated/WidgetsClient.cs", "public sealed class WidgetsClient(IWidgetService widgets);", false),
            ("Src/Web/packages/api-client/src/schema.ts", "\"/api/v1/widgets\": {", false),
            ("Tests/Integration/A.Api.IntegrationTests/OpenApi/v1.json", "\"/api/v1/widgets\": {", false),
            ("Src/Infrastructure/A.Infrastructure.Persistence/Migrations/20240102_AddWidgets.cs", "using Acme.Persistence.Features.Widgets.Rows.Models;", false),
            ("Src/Presentation/A.Api/Noise.cs", "// Widgets come from WidgetService; var label = \"WidgetService\";", false),
        ];

        foreach (var group in lines.GroupBy(line => line.Path))
        {
            Put(root, group.Key, string.Join("\n", group.Select(line => line.Text)) + "\n");
        }

        Commit(root);

        var report = Invoke(root, "Widgets");

        foreach (var (path, text, listed) in lines)
        {
            string[] all = File.ReadAllLines(Path.Combine(root, path));
            string cited = $"{path}:{Array.IndexOf(all, text) + 1}  {text.Trim()}";

            expect(
                report.Output.Contains(cited, StringComparison.Ordinal) == listed,
                listed ? $"The report does not list what still names the feature: {cited}" : $"The report lists what does not name the feature, or what is regenerated: {cited}");
        }

        expect(report.Output.Contains("Blockers: none.", StringComparison.Ordinal), $"A line of configuration or documentation blocked the removal: {report.Output}");

        var applied = Invoke(root, "Widgets", "--apply");

        expect(
            applied.Code == 0 && applied.Output.Contains("left to you: 4 line(s)", StringComparison.Ordinal),
            $"--apply did not remove the feature, or did not print what is left to do by hand: {applied.Output}{applied.Error}");
    }

    private static string Json(string[] lines) => new JsonArray([.. lines.Select(line => (JsonNode)JsonValue.Create(line)!)]).ToJsonString();

    private static string Edit(string path, string anchor, string position, string[] lines, string[]? replaced = null) =>
        $"{{ \"path\": \"{path}\", \"anchor\": {JsonValue.Create(anchor)!.ToJsonString()}, \"position\": \"{position}\", \"lines\": {Json(lines)}"
        + (replaced is null ? " }" : $", \"replaced\": {Json(replaced)} }}");

    private static void UndoCases(string scratch, Action<bool, string> expect)
    {
        const string Settings = "Src/Presentation/A.Api/appsettings.json";
        const string Program = "Src/Presentation/A.Api/Program.cs";
        string edits = string.Join(
            ", ",
            Edit(Program, "builder.Services.AddCacheStore();", "before", ["builder.Services.AddWidgets();", string.Empty]),
            Edit(Settings, "  },", "after", ["  \"WidgetLimits\": {", "    \"MaxItems\": 3", "  },"]),
            Edit(Settings, "}", "before", ["  \"Seed\": 1"], ["  \"Seed\": 0"]));

        string root = Skeleton(scratch, edits);
        string program = Path.Combine(root, Program);
        string settings = Path.Combine(root, Settings);

        Put(root, Settings, "{\n  \"Logging\": {\n    \"Level\": 1\n  },\n  \"WidgetLimits\": {\n    \"MaxItems\": 3\n  },\n  \"Seed\": 1\n}\n");
        File.WriteAllText(program, "builder.Services.AddTodoLists();\n\nbuilder.Services.AddWidgets();\n\nbuilder.Services.AddCacheStore();\n");
        Commit(root);

        var report = Invoke(root, "Widgets");

        expect(report.Output.Contains($"{Settings}: 1 line in place of 1 line before '}}'", StringComparison.Ordinal), $"The report does not show an edit that replaced a line: {report.Output}");

        var applied = Invoke(root, "Widgets", "--apply");

        expect(applied.Code == 0, $"Recorded edits were not undone: {applied.Output}{applied.Error}");
        expect(File.ReadAllText(program) == _program, $"Undoing an insertion does not give back the file: {File.ReadAllText(program)}");
        expect(
            File.ReadAllText(settings) == "{\n  \"Logging\": {\n    \"Level\": 1\n  },\n  \"Seed\": 0\n}\n",
            $"Undoing an edit that replaced a line, then the one made before it, does not give back the file: {File.ReadAllText(settings)}");

        const string Composition = "Tests/Architecture/A.Architecture.Tests/Composition/HostComposition.cs";
        string twin = Edit(Composition, "        services.AddPurgeExpiredIdempotencyKeys();", "before", ["        services.AddWidgets();"]);
        string twins = Skeleton(scratch, $"{twin}, {twin}");
        const string Composed = "    {\n        services.AddWidgets();\n        services.AddGadgets();\n        services.AddPurgeExpiredIdempotencyKeys();\n    }\n";

        Put(twins, Composition, Composed + Composed);
        Commit(twins);

        var twinsApplied = Invoke(twins, "Widgets", "--apply");
        const string Left = "    {\n        services.AddGadgets();\n        services.AddPurgeExpiredIdempotencyKeys();\n    }\n";

        expect(twinsApplied.Code == 0, $"The same edit, made beside each of two equal lines, was not undone: {twinsApplied.Output}{twinsApplied.Error}");
        expect(File.ReadAllText(Path.Combine(twins, Composition)) == Left + Left, $"Undoing the same edit made twice does not give back the file: {File.ReadAllText(Path.Combine(twins, Composition))}");

        string moved = Skeleton(scratch, Edit(Program, "builder.Services.AddCacheStore();", "before", ["builder.Services.AddWidgets();", string.Empty]));

        File.WriteAllText(Path.Combine(moved, Program), "builder.Services.AddWidgets();\n\nbuilder.Services.AddTodoLists();\n\nbuilder.Services.AddGadgets();\n\nbuilder.Services.AddCacheStore();\n");
        Commit(moved);

        expect(Invoke(moved, "Widgets", "--apply").Code == 0, "A recorded block found once, though no longer beside its line, was not undone.");
        expect(
            File.ReadAllText(Path.Combine(moved, Program)) == "builder.Services.AddTodoLists();\n\nbuilder.Services.AddGadgets();\n\nbuilder.Services.AddCacheStore();\n",
            $"A recorded block found once was not the one removed: {File.ReadAllText(Path.Combine(moved, Program))}");

        string changed = Skeleton(scratch, Edit(Program, "builder.Services.AddCacheStore();", "before", ["builder.Services.AddWidgets();", string.Empty]));

        File.WriteAllText(Path.Combine(changed, Program), "builder.Services.AddTodoLists();\n\nbuilder.Services.AddWidgets(options => options.Strict = true);\n\nbuilder.Services.AddCacheStore();\n");
        Commit(changed);
        Refused(expect, "a recorded line changed by hand", changed, "Widgets", "still use its types", $"{Program}:3");

        string squeezed = Skeleton(scratch, Edit(Program, "builder.Services.AddCacheStore();", "before", ["builder.Services.AddWidgets();", string.Empty]));

        File.WriteAllText(Path.Combine(squeezed, Program), "builder.Services.AddTodoLists();\n\nbuilder.Services.AddWidgets();\nbuilder.Services.AddCacheStore();\n");
        Commit(squeezed);
        Refused(expect, "a recorded block whose blank line was removed", squeezed, "Widgets", "no longer there as they were written");

        string twice = Skeleton(scratch, Edit(Program, "builder.Services.AddCacheStore();", "before", ["builder.Services.AddWidgets();", string.Empty]));

        File.WriteAllText(Path.Combine(twice, Program), "builder.Services.AddWidgets();\n\nbuilder.Services.AddCacheStore();\n\nbuilder.Services.AddWidgets();\n\nbuilder.Services.AddCacheStore();\n");
        Commit(twice);
        Refused(expect, "a recorded block beside two copies of its line", twice, "Widgets", "no longer there as they were written");

        string gone = Skeleton(scratch, Edit(Program, "builder.Services.AddCacheStore();", "before", ["builder.Services.AddWidgets();", string.Empty]));
        var goneReport = Invoke(gone, "Widgets");
        var goneApplied = Invoke(gone, "Widgets", "--apply");

        expect(goneReport.Output.Contains($"Undone: {Program}", StringComparison.Ordinal), $"An edit someone had undone was not reported as gone: {goneReport.Output}");
        expect(goneApplied.Code == 0 && File.ReadAllText(Path.Combine(gone, Program)) == _program, "An edit someone had undone failed the removal, or changed the file.");
    }

    private static string SourceFile([CallerFilePath] string path = "") => path;

    private static int Generate(string root, params string[] args)
    {
        string generator = Path.Combine(Path.GetDirectoryName(SourceFile())!, "NewFeature.cs");
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };

        foreach (string argument in (string[])["run", generator, .. args, "--root", root])
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync();

        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        errors.Wait();

        return process.ExitCode;
    }

    private static void Projects(string root)
    {
        foreach (string project in new[]
        {
            "Src/Domain/A.Domain", "Src/Application/A.Application", "Src/Infrastructure/A.Infrastructure.Persistence",
            "Src/Presentation/A.Api", "Tests/Application/A.Application.UnitTests", "Tests/Domain/A.Domain.UnitTests",
            "Tests/Infrastructure/A.Infrastructure.Persistence.UnitTests", "Tests/Presentation/A.Api.UnitTests",
        })
        {
            Put(root, $"{project}/Acme{Path.GetFileName(project)[1..]}.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
        }

        Put(root, "cleanarch.json", "{\n  \"name\": \"Acme\",\n  \"options\": {\n    \"database\": \"postgresql\"\n  }\n}\n");
        Put(root, "Tools/Tasks.cs", "return 0;\n");
        Put(root, "Src/Infrastructure/A.Infrastructure.Persistence/Migrations/AppDbContextModelSnapshot.cs", "// the initial migration's snapshot\n");
        Put(root, "Src/Infrastructure/A.Infrastructure.Persistence/Common/Contexts/AppDbContext.cs", _appDbContext);
        Put(root, "Src/Infrastructure/A.Infrastructure.Persistence/PersistenceModule.cs", "        AddIdempotencyFeature(services, configuration);\n    private static void AddIdempotencyFeature(\n");
        Put(root, "Src/Application/A.Application/ApplicationModule.cs", "    private static void AddFeature(this IServiceCollection services, string vertical)\n");
        Put(root, "Tests/Architecture/A.Architecture.Tests/Composition/HostComposition.cs", _hostComposition);
        Put(root, "docs/README.md", "## Features\n\n| Page | Answers |\n|---|---|\n");
        //#if (api)
        Put(root, "docs/reference/api.md", "## Routes\n\n## Error codes\n\n### Resources\n\n| `code` | Status | When |\n|---|---|---|\n");
        //#endif
    }

    private const string _appDbContext =
        "    public const string PlatformSchema = \"platform\";\n"
        + "    internal DbSet<IdempotencyRecord> IdempotencyKeys => Set<IdempotencyRecord>();\n"
        + "        modelBuilder.ApplyConfiguration(new IdempotencyRecordConfiguration());\n";

    private const string _hostComposition =
        "        services.AddPurgeExpiredIdempotencyKeys();\n"
        + "        services.AddScoped<ICurrentAccessToken, ArchitectureTestCurrentAccessToken>();\n"
        + "internal sealed class ArchitectureTestBackgroundAuditActor : IAuditActor;\n";

    private static string Tree(string scratch, Action<string>? ground = null)
    {
        string root = Path.Combine(scratch, Guid.CreateVersion7().ToString("N"));

        Projects(root);
        Put(root, "Src/Presentation/A.Api/Program.cs", _program);
        Put(root, "Src/Application/A.Application.Core/Common/Ports/IDeletedAccountDataPolicy.cs", "// the reaction this tree already ships\n");
        ground?.Invoke(root);
        Commit(root);

        return root;
    }

    private static void GeneratorCases(string scratch, Action<bool, string> expect)
    {
        string[][] combinations =
        [
            ["Widgets", "Widget", "--access", "owner", "--deletion", "delete", "--model", "aggregate", "--concurrency", "etag"],
            ["Notes", "Note", "--access", "shared", "--operations", "get,list", "--concurrency", "none"],
            ["Gadgets", "Gadget", "--access", "owner", "--deletion", "anonymise", "--model", "record", "--concurrency", "etag", "--operations", "get,list,create,update"],
        ];

        foreach (string[] combination in combinations)
        {
            string root = Tree(scratch);
            string features = combination[0];
            var original = Snapshot(root);

            expect(Generate(root, combination) == 0, $"The generator refused {features}.");
            Commit(root);

            var generated = Snapshot(root);
            var report = Invoke(root, features);

            expect(report.Code == 0 && Same(generated, Snapshot(root)) && report.Output.Contains("Blockers: none.", StringComparison.Ordinal), $"The report on a fresh {features} changed the tree or found a blocker: {report.Output}");
            expect(!report.Output.Contains("undoes the 0 edit(s)", StringComparison.Ordinal), $"The report on a composed {features} lists no edit to undo: {report.Output}");

            string program = Path.Combine(root, "Src/Presentation/A.Api/Program.cs");
            string registered = File.ReadAllText(program);

            File.AppendAllText(program, $"builder.Services.Add{features}();\n");
            Commit(root);
            Refused(expect, $"{features} registered a second time by hand", root, features, "still use its types", "Src/Presentation/A.Api/Program.cs:");
            File.WriteAllText(program, registered);

            string edited = Directory.EnumerateFiles(Path.Combine(root, "Src/Application/A.Application/Features", features), "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal).First();

            File.AppendAllText(edited, "// what the team changed\n");
            Commit(root);

            var ran = new List<TextEdits.Command>();
            var applied = Invoke(root, Recording(ran), features, "--apply");
            var after = Snapshot(root);

            expect(applied.Code == 0, $"A composed {features}, one of its files edited, was refused: {applied.Output}{applied.Error}");
            expect(Same(original, after), $"After removing {features} the tree is not the one that was there before it was generated: {Differences(original, after)}");
            expect(
                Launched(ran).SequenceEqual(["dotnet run Tools/Tasks.cs migration-check", "dotnet run Tools/Tasks.cs build", "dotnet run Tools/Tasks.cs -- openapi --no-build"]),
                $"Removing {features} did not check the model, then build once and rewrite the OpenAPI reference from that build: {string.Join(" | ", Launched(ran))}");

            foreach (string empty in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).Where(folder => !folder.Contains($"{Path.DirectorySeparatorChar}.git", StringComparison.Ordinal) && !Directory.EnumerateFileSystemEntries(folder).Any()))
            {
                expect(false, $"After removing {features}, its last feature, '{Path.GetRelativePath(root, empty)}' was left empty.");
            }
        }
    }

    private static void MigrationCases(string scratch, Action<bool, string> expect)
    {
        const string Migrations = "Src/Infrastructure/A.Infrastructure.Persistence/Migrations";
        const string Added = $"{Migrations}/20260101000000_AddWidgets.cs";
        string root = Tree(scratch);

        expect(Generate(root, "Widgets", "Widget", "--access", "shared") == 0, "The generator refused Widgets for the migration case.");
        Put(root, Added, "            migrationBuilder.EnsureSchema(\n                name: \"widgets\");\n");
        Commit(root);

        var report = Invoke(root, "Widgets");

        expect(report.Output.Contains("RemoveWidgets drops the tables of Widgets and its schema \"widgets\"; AddWidgets, which a database may have applied, stays as it is.", StringComparison.Ordinal), $"The report does not say which migration --apply adds: {report.Output}");

        var ran = new List<TextEdits.Command>();
        string addedBefore = Sha(root, Added);
        var applied = Invoke(root, Recording(ran), "Widgets", "--apply");
        string removal = Path.Combine(root, Migrations, "20990101000000_RemoveWidgets.cs");

        expect(applied.Code == 0, $"A feature with its migration was refused: {applied.Output}{applied.Error}");
        expect(
            Launched(ran).SequenceEqual(["dotnet run Tools/Tasks.cs migration-check", "dotnet run Tools/Tasks.cs build", "dotnet run Tools/Tasks.cs -- migration-add RemoveWidgets --no-build", "dotnet run Tools/Tasks.cs -- openapi --no-build"]),
            $"--apply did not check, build once, then add RemoveWidgets and rewrite the OpenAPI reference from that build: {string.Join(" | ", Launched(ran))}");
        expect(File.Exists(Path.Combine(root, Added)) && Sha(root, Added) == addedBefore, "The migration that added the feature, which a database may have applied, was deleted or rewritten.");

        string written = File.Exists(removal) ? File.ReadAllText(removal) : string.Empty;

        expect(
            written.Contains("                schema: \"widgets\");\n\n            migrationBuilder.DropSchema(\n                name: \"widgets\");\n        }\n", StringComparison.Ordinal),
            $"RemoveWidgets does not drop the schema after its tables: {written}");
        expect(
            written.Contains("        {\n            migrationBuilder.EnsureSchema(\n                name: \"widgets\");\n\n            migrationBuilder.CreateTable(", StringComparison.Ordinal),
            $"The Down of RemoveWidgets does not ensure the schema before it creates the tables again: {written}");

        string again = Tree(scratch);

        expect(Generate(again, "Widgets", "Widget", "--access", "shared") == 0, "The generator refused Widgets for the second removal.");
        Put(again, Added, "// no schema of its own\n");
        Put(again, $"{Migrations}/20250101000000_RemoveWidgets.cs", "// an earlier removal\n");
        Commit(again);

        var second = new List<TextEdits.Command>();

        expect(Invoke(again, Recording(second), "Widgets", "--apply").Code == 0, "A feature removed once before was refused.");
        expect(Launched(second).Contains("dotnet run Tools/Tasks.cs -- migration-add RemoveWidgets2 --no-build"), $"A second removal did not name its migration apart from the first: {string.Join(" | ", Launched(second))}");
        expect(!File.ReadAllText(Path.Combine(again, Migrations, "20990101000000_RemoveWidgets2.cs")).Contains("DropSchema", StringComparison.Ordinal), "A schema the feature's migration did not create was dropped.");

        string widened = Skeleton(scratch);
        string manifestFile = Path.Combine(widened, FeatureManifest.Folder, "Widgets.json");
        string changed = _skeletonFiles[1];

        File.WriteAllText(
            manifestFile,
            File.ReadAllText(manifestFile).Replace(
                "\n  ]\n}\n",
                $"\n  ],\n  \"addedFields\": [{{ \"declaration\": \"Code:string(20)='none'\", \"migration\": \"AddCodeToWidgets\", \"files\": [{{ \"path\": \"{changed}\", \"before\": \"{new string('a', 64)}\", \"after\": \"{Sha(widened, changed)}\", \"changes\": [\"Code\"] }}] }}]\n}}\n",
                StringComparison.Ordinal));
        Put(widened, $"{Migrations}/20260101000000_AddWidgets.cs", "// the feature's migration\n");
        Put(widened, $"{Migrations}/20260102000000_AddCodeToWidgets.cs", "// the field's migration\n");
        Commit(widened);

        var widenedReport = Invoke(widened, "Widgets");

        expect(
            widenedReport.Output.Contains("Code:string(20)='none', in 1 file, with its migration AddCodeToWidgets", StringComparison.Ordinal)
                && widenedReport.Output.Contains("AddWidgets, AddCodeToWidgets, which a database may have applied, stay as they are.", StringComparison.Ordinal),
            $"The report does not show the field new-field added, or says its migration goes: {widenedReport.Output}");
        expect(Invoke(widened, "Widgets", "--apply").Code == 0 && !File.Exists(Path.Combine(widened, changed)), "A feature that took a field from new-field was refused, or the file the field changed was left.");
        expect(File.Exists(Path.Combine(widened, $"{Migrations}/20260102000000_AddCodeToWidgets.cs")), "The migration new-field added, which a database may have applied, was deleted.");

        string ensured = Tree(scratch);

        expect(Generate(ensured, "Widgets", "Widget", "--access", "shared") == 0, "The generator refused Widgets for the ensured schema.");
        Put(ensured, Added, "            migrationBuilder.EnsureSchema(\n                name: \"widgets\");\n");
        Commit(ensured);
        expect(Invoke(ensured, Recording([], ensured: true), "Widgets", "--apply").Code == 0, "A feature whose removal migration already ensures its schema was refused.");

        string kept = File.ReadAllText(Path.Combine(ensured, Migrations, "20990101000000_RemoveWidgets.cs"));

        expect(
            Regex.Count(kept, "EnsureSchema", RegexOptions.None) == 1 && kept.Contains("DropSchema", StringComparison.Ordinal),
            $"A Down that already ensures the schema was given a second EnsureSchema, or the Up lost its DropSchema: {kept}");

        string failed = Tree(scratch);

        expect(Generate(failed, "Widgets", "Widget", "--access", "shared") == 0, "The generator refused Widgets for the failed build.");
        Commit(failed);

        var stopped = Invoke(failed, Recording([], "dotnet run Tools/Tasks.cs build"), "Widgets", "--apply");

        expect(
            stopped.Code == 1 && stopped.Error.Contains("What remains", StringComparison.Ordinal) && stopped.Error.Contains("openapi --no-build", StringComparison.Ordinal),
            $"A failed build did not name what remains, the OpenAPI reference among it: {stopped.Error}");
    }

    private static void OrderCases(string scratch, Action<bool, string> expect)
    {
        string[] widgets = ["Widgets", "Widget", "--access", "shared", "--model", "aggregate", "--concurrency", "etag"];
        string[] gadgets = ["Gadgets", "Gadget", "--access", "shared"];

        static void Usings(string root)
        {
            foreach (string file in new[]
            {
                "Src/Presentation/A.Api/Program.cs", "Src/Application/A.Application/ApplicationModule.cs",
                "Src/Infrastructure/A.Infrastructure.Persistence/Common/Contexts/AppDbContext.cs", "Src/Infrastructure/A.Infrastructure.Persistence/PersistenceModule.cs",
                "Tests/Architecture/A.Architecture.Tests/Composition/HostComposition.cs",
            })
            {
                Put(root, file, "using Acme.Application;\nusing System;\n\n" + File.ReadAllText(Path.Combine(root, file)));
            }
        }

        string alone = Tree(scratch, Usings);

        expect(Generate(alone, gadgets) == 0, "The generator refused Gadgets alone.");

        string both = Tree(scratch, Usings);
        var original = Snapshot(both);

        expect(Generate(both, widgets) == 0 && Generate(both, gadgets) == 0, "The generator refused Widgets then Gadgets.");
        Commit(both);
        expect(Invoke(both, "Widgets", "--apply").Code == 0, "The first of two features was refused.");

        static Dictionary<string, string> WithoutManifests(Dictionary<string, string> tree) =>
            tree.Where(pair => !pair.Key.StartsWith(FeatureManifest.Folder, StringComparison.Ordinal)).ToDictionary(StringComparer.Ordinal);

        expect(
            Same(WithoutManifests(Snapshot(alone)), WithoutManifests(Snapshot(both))),
            $"Removing the first of two features does not leave the tree the second alone writes: {Differences(WithoutManifests(Snapshot(alone)), WithoutManifests(Snapshot(both)))}");

        Commit(both);
        expect(Invoke(both, "Gadgets", "--apply").Code == 0 && Same(original, Snapshot(both)), "After removing both, first in first out, the tree is not the one they were written in.");
    }

    private static string BareTree(string scratch, string adapter = "Auth")
    {
        string root = Path.Combine(scratch, Guid.CreateVersion7().ToString("N"));

        Projects(root);

        Put(root, "Src/Presentation/A.Api/Program.cs", "builder.Services.AddPurgeExpiredIdempotencyKeys();\n");
        Put(root, "Src/Application/A.Application.Core/Common/Ports/ICurrentUser.cs", "public interface ICurrentUser;\n");
        Put(root, "Src/Application/A.Application.Core/ApplicationCoreModule.cs", "    public static IServiceCollection AddUseCasePipeline(this IServiceCollection services) => services;\n");
        Put(root, "Src/Application/A.Application.Core/Common/Events/UserAccountDeleted.cs", "/// rather than trusting delivery, see <c>IDeletedAccountDataPolicy</c>;\npublic sealed record UserAccountDeleted;\n");
        Put(root, "Src/Presentation/A.Api/Features/BackgroundJobs/Jobs/BackgroundJobsModule.cs", "services.AddMaintenanceJobs(configuration);\n");
        Put(
            root,
            adapter == "Auth"
                ? "Src/Presentation/A.Api/Features/Auth/Composition/AuthComposition.cs"
                : "Src/Presentation/A.Api/Features/ExternalIdentity/Composition/ExternalAuthenticationComposition.cs",
            adapter == "Auth" ? "        services.AddAuthModule(configuration);\n" : "        services.AddExternalIdentityPersistence(configuration);\n");
        Commit(root);

        return root;
    }

    private static readonly string[] _sharedFiles =
    [
        "Src/Application/A.Application.Core/Common/Ports/IDeletedAccountDataPolicy.cs",
        "Src/Application/A.Application.Core/Common/Ports/IUserAccountDirectory.cs",
        "Src/Application/A.Application.Core/Features/DeletedAccounts/UseCases/Commands/SweepDeletedAccountData/SweepDeletedAccountData.cs",
        "Src/Presentation/A.Api/Features/BackgroundJobs/Jobs/DeletedAccountSweepJobsExtensions.cs",
        "Src/Presentation/A.Api/Features/BackgroundJobs/Jobs/DeletedAccountSweepOptions.cs",
        "Src/Presentation/A.Api/Features/Auth/Composition/AccountUserAccountDirectory.cs",
    ];

    private static void SharedFilesCases(string scratch, Action<bool, string> expect)
    {
        string[] widgets = ["Widgets", "Widget", "--access", "owner", "--deletion", "delete"];

        string alone = BareTree(scratch);
        var original = Snapshot(alone);

        expect(Generate(alone, widgets) == 0, "The generator refused Widgets in a bare tree.");
        expect(_sharedFiles.All(file => File.Exists(Path.Combine(alone, file))), "The first owner feature did not bring the shared files.");
        Commit(alone);

        var report = Invoke(alone, "Widgets");

        expect(report.Output.Contains("Blockers: none.", StringComparison.Ordinal), $"(a) the report on the only owner feature found a blocker: {report.Output}");
        expect(_sharedFiles.All(file => report.Output.Contains(file, StringComparison.Ordinal)), "(a) the report does not list the shared files.");

        var applied = Invoke(alone, "Widgets", "--apply");

        expect(applied.Code == 0, $"(a) the only owner feature was refused: {applied.Output}{applied.Error}");
        expect(Same(original, Snapshot(alone)), $"(a) after removing the only owner feature the tree is not the bare one: {Differences(original, Snapshot(alone))}");

        string WithWidgets(string adapter = "Auth")
        {
            string tree = BareTree(scratch, adapter);

            expect(Generate(tree, widgets) == 0, "The generator refused Widgets in a bare tree.");
            Commit(tree);

            return tree;
        }

        string two = BareTree(scratch);
        var bare = Snapshot(two);

        expect(Generate(two, widgets) == 0 && Generate(two, "Sprockets", "Sprocket", "--access", "owner", "--deletion", "anonymise") == 0, "(b) the generator refused a feature.");
        Commit(two);
        Refused(expect, "(b) a second owner feature", two, "Widgets", "Sprockets (generated, --access owner)", "error: Widgets holds the shared account-deletion files that Sprockets still depend on.");
        expect(Invoke(two, "Sprockets", "--apply").Code == 0, "(b) the second owner feature was refused.");
        Commit(two);
        expect(Invoke(two, "Widgets", "--apply").Code == 0, "(b) the first owner feature was refused once it was the only one.");
        expect(Same(bare, Snapshot(two)), $"(b) after removing both owner features the tree is not the bare one: {Differences(bare, Snapshot(two))}");

        string notes = BareTree(scratch);
        var beforeNotes = Snapshot(notes);

        expect(Generate(notes, widgets) == 0 && Generate(notes, "Notes", "Note", "--access", "shared") == 0, "(c) the generator refused a feature.");
        Commit(notes);

        var withNotes = Snapshot(notes);

        expect(Invoke(notes, "Widgets", "--apply").Code == 0, "(c) a shared feature held the owner feature back.");
        expect(
            withNotes.Where(pair => pair.Key.Contains("/Features/Notes", StringComparison.Ordinal))
                .All(pair => Snapshot(notes).TryGetValue(pair.Key, out string? hash) && hash == pair.Value),
            "(c) removing Widgets touched the files of Notes.");
        Commit(notes);
        expect(Invoke(notes, "Notes", "--apply").Code == 0, "(c) Notes was refused.");
        expect(Same(beforeNotes, Snapshot(notes)), $"(c) after removing both features the tree is not the bare one: {Differences(beforeNotes, Snapshot(notes))}");

        string journal = WithWidgets();

        Put(
            journal,
            "Src/Application/A.Application/Features/Journal/Services/Consumers/DeleteJournalOnAccountDeletedConsumer.cs",
            "// Deletes a journal when its account is.\ninternal sealed class DeleteJournalOnAccountDeletedConsumer(IUserAccountDirectory accounts) : IDeletedAccountDataPolicy;\n");
        Commit(journal);
        Refused(expect, "(d) a hand-made owner feature", journal, "Widgets", "Journal (no manifest)");

        string[] registrations =
        [
            "Src/Application/A.Application.Core/ApplicationCoreModule.cs|public static IServiceCollection AddDeletedAccountSweep(this IServiceCollection services, int again)",
            "Src/Presentation/A.Api/Features/BackgroundJobs/Jobs/BackgroundJobsModule.cs|services.AddDeletedAccountSweepJobs(configuration, again: true);",
            "Tests/Architecture/A.Architecture.Tests/Composition/HostComposition.cs|internal sealed class SecondUserAccountDirectory : IUserAccountDirectory",
        ];

        foreach (string registration in registrations)
        {
            string[] parts = registration.Split('|');
            string tree = WithWidgets();

            File.AppendAllText(Path.Combine(tree, parts[0]), parts[1] + "\n");
            Commit(tree);
            Refused(expect, $"(e) {parts[1]}", tree, "Widgets", "still register", $"{parts[0]}:{File.ReadAllLines(Path.Combine(tree, parts[0])).Length}");
        }

        string edited = WithWidgets();
        var bareEdited = Snapshot(BareTree(scratch));

        File.AppendAllText(Path.Combine(edited, _sharedFiles[1]), "// edited\n");
        Put(edited, "Src/Application/A.Application.Core/Features/DeletedAccounts/UseCases/Commands/Extra.cs", "x\n");
        Commit(edited);

        var editedReport = Invoke(edited, "Widgets");

        expect(
            editedReport.Output.Contains($"Edited: {_sharedFiles[1]}", StringComparison.Ordinal) && editedReport.Output.Contains("Added: Src/Application/A.Application.Core/Features/DeletedAccounts/UseCases/Commands/Extra.cs", StringComparison.Ordinal),
            $"(f) a shared file edited or added beside the sweep was not listed: {editedReport.Output}");
        expect(Invoke(edited, "Widgets", "--apply").Code == 0, "(f) an edited shared file held the only owner feature back.");
        expect(bareEdited.Keys.Where(key => !key.EndsWith("Tasks.cs", StringComparison.Ordinal)).All(key => Snapshot(edited).ContainsKey(key)) && !_sharedFiles.Any(file => File.Exists(Path.Combine(edited, file))), "(f) the shared files, edited or added to, were not removed with the only owner feature.");

        string external = BareTree(scratch, "ExternalIdentity");
        var beforeExternal = Snapshot(external);

        expect(Generate(external, widgets) == 0, "(h) the generator refused Widgets in an externally authenticated bare tree.");
        expect(File.Exists(Path.Combine(external, "Src/Presentation/A.Api/Features/ExternalIdentity/Composition/ExternalIdentityUserAccountDirectory.cs")), "(h) the external account directory was not written.");
        Commit(external);
        expect(Invoke(external, "Widgets", "--apply").Code == 0, "(h) the only owner feature of an externally authenticated tree was refused.");
        expect(Same(beforeExternal, Snapshot(external)), $"(h) after removing it the tree is not the bare one: {Differences(beforeExternal, Snapshot(external))}");

        string stale = WithWidgets();

        Put(stale, $"{FeatureManifest.Folder}/Gizmos.json", "{ \"schemaVersion\": 1, \"feature\": \"Gizmos\", \"type\": \"Gizmo\", \"options\": { \"access\": \"owner\" }, \"files\": [] }\n");
        Commit(stale);
        Refused(expect, "(i) a stale owner manifest", stale, "Widgets", "Gizmos (generated, --access owner)");

        string unreadable = WithWidgets();

        Put(unreadable, $"{FeatureManifest.Folder}/Gizmos.json", "{ not json\n");
        Commit(unreadable);
        Refused(expect, "an unreadable manifest", unreadable, "Widgets", "nothing can tell whether Gizmos depends");
    }

    private static string PagedTree(string scratch, Action<string>? ground = null)
    {
        string template = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(SourceFile())!)!, "docs", "_page-templates", "feature.md");

        return Tree(scratch, root =>
        {
            Put(root, "docs/_page-templates/feature.md", File.ReadAllText(template));
            ground?.Invoke(root);
        });
    }

    private static void PageCases(string scratch, Action<bool, string> expect)
    {
        string pagePath = PagePath("Widgets");
        string[] widgets = ["Widgets", "Widget", "--access", "shared"];

        string fresh = PagedTree(scratch);
        var original = Snapshot(fresh);

        expect(Generate(fresh, widgets) == 0 && File.Exists(Path.Combine(fresh, pagePath)), "The generator did not write the feature page.");
        Commit(fresh);

        var report = Invoke(fresh, "Widgets");

        expect(report.Output.Contains($"Feature page: {pagePath}, written by the generator", StringComparison.Ordinal), $"The report does not name the page: {report.Output}");
        expect(report.Output.Contains("docs/README.md: 1 line after", StringComparison.Ordinal), $"The report does not list the row of docs/README.md among the edits it undoes: {report.Output}");
        expect(!report.Output.Contains($"{pagePath}:", StringComparison.Ordinal), "The page's own lines were listed as matches.");

        var applied = Invoke(fresh, "Widgets", "--apply");

        expect(applied.Code == 0, $"A feature with its page was refused: {applied.Output}{applied.Error}");
        expect(Same(original, Snapshot(fresh)), $"After removing the feature, its page, its row or the folder of pages was left behind, or the template went with it: {Differences(original, Snapshot(fresh))}");

        string edited = PagedTree(scratch);

        expect(Generate(edited, widgets) == 0, "The generator refused Widgets for the edited page.");
        File.AppendAllText(Path.Combine(edited, pagePath), "\nWidgets are what the warehouse ships.\n");
        Commit(edited);
        expect(Invoke(edited, "Widgets", "--apply").Code == 0 && !File.Exists(Path.Combine(edited, pagePath)), "An edited page held the removal back, or was left behind.");

        string gone = PagedTree(scratch);

        expect(Generate(gone, widgets) == 0, "The generator refused Widgets for the deleted page.");
        File.Delete(Path.Combine(gone, pagePath));
        Commit(gone);

        var goneApplied = Invoke(gone, "Widgets", "--apply");

        expect(goneApplied.Code == 0, $"A page the team had deleted failed the removal: {goneApplied.Output}{goneApplied.Error}");

        string own = PagedTree(scratch, root => Put(root, pagePath, "# Widgets, as the team wrote them\n"));

        expect(Generate(own, widgets) == 0, "The generator refused Widgets beside a page the team wrote.");
        Commit(own);
        expect(Invoke(own, "Widgets", "--apply").Code == 0, "Widgets beside a page the team wrote was refused.");
        expect(File.Exists(Path.Combine(own, pagePath)), "A page the generator did not write was deleted.");

        string forged = PagedTree(scratch);

        expect(Generate(forged, widgets) == 0, "The generator refused Widgets for the forged page.");

        string manifest = Path.Combine(forged, FeatureManifest.Folder, "Widgets.json");

        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace(pagePath, "docs/README.md", StringComparison.Ordinal));
        Commit(forged);
        Refused(expect, "a manifest naming another page", forged, "Widgets", $"where the generator writes '{pagePath}'");
    }

    private static void NameCollisionCases(string scratch, Action<bool, string> expect)
    {
        const string Noise =
            "var sorted = items.OrderBy(x => x).Order(); var groups = items.GroupBy(x => x); IOrderedQueryable<int> ordered = query;\n"
            + "var source = new TaskCompletionSource(); var tags = new TagSet(); var helpers = TagHelpers.Create(); var item = list.Item(0);\n"
            + "var text = \"GetNote, GetTodoList and GetNoteRequest\"; // GetNoteRequest, INoteService, CreateGroupRequest\n";

        foreach (string[] row in new[]
        {
            new[] { "Notes", "Note" }, new[] { "Orders", "Order" }, new[] { "Tags", "Tag" },
            new[] { "Groups", "Group" }, new[] { "Tasks", "Task" }, new[] { "Items", "Item" },
        })
        {
            foreach (string model in new[] { "record", "aggregate" })
            {
                string root = Tree(scratch, tree =>
                {
                    Put(tree, "Src/Presentation/A.Api/Noise.cs", Noise);
                    Put(tree, "Tests/Presentation/A.Api.UnitTests/ControllerContractTests.cs", Noise);
                });

                string label = $"{row[0]} ({model})";

                expect(Generate(root, row[0], row[1], "--access", "shared", "--model", model, "--concurrency", model == "aggregate" ? "etag" : "none") == 0, $"The generator refused {label}.");
                Commit(root);

                var report = Invoke(root, row[0]);

                expect(
                    report.Output.Contains("Code outside the feature that still uses it, recorded nowhere: none.", StringComparison.Ordinal),
                    $"{label}: a word that merely starts like the type, or the type in a string or comment, was taken for a use: {report.Output}");
                expect(Invoke(root, row[0], "--apply").Code == 0, $"{label}: a fresh feature was refused.");
            }
        }

        string registered = Tree(scratch);

        expect(Generate(registered, "Widgets", "Widget", "--access", "owner", "--deletion", "delete") == 0, "The generator refused Widgets for the use case.");

        string consumer = Directory.EnumerateFiles(registered, "*OnAccountDeleted*.cs", SearchOption.AllDirectories).Select(Path.GetFileNameWithoutExtension).FirstOrDefault() ?? "none";

        Put(registered, "Src/Presentation/A.Api/Wiring.cs", $"services.AddScoped<{consumer}>();\n");
        Commit(registered);
        expect(consumer != "none", "No consumer of the account deletion was written.");
        Refused(expect, $"a consumer registered elsewhere ({consumer})", registered, "Widgets", "still use its types", "Src/Presentation/A.Api/Wiring.cs:1");
    }

    private static void WithoutTheApiCases(string scratch, Action<bool, string> expect)
    {
        string[][] combinations =
        [
            ["Widgets", "Widget", "--access", "owner", "--deletion", "delete", "--model", "aggregate", "--concurrency", "etag"],
            ["Notes", "Note", "--access", "shared", "--operations", "get,list"],
        ];

        foreach (string[] combination in combinations)
        {
            string root = Tree(scratch, tree =>
            {
                Directory.Delete(Path.Combine(tree, "Src/Presentation/A.Api"), recursive: true);
                Directory.Delete(Path.Combine(tree, "Tests/Presentation/A.Api.UnitTests"), recursive: true);
                Put(tree, "Src/Presentation/A.Portal/Program.cs", _program);
            });
            string features = combination[0];
            var original = Snapshot(root);

            expect(Generate(root, combination) == 0, $"The generator refused {features} in a tree without the API.");
            Commit(root);

            var report = Invoke(root, features);

            expect(report.Code == 0 && report.Output.Contains("Blockers: none.", StringComparison.Ordinal), $"The report on a fresh {features} without the API found a blocker: {report.Output}");
            expect(!report.Output.Contains("openapi", StringComparison.Ordinal), $"The report on {features} without the API plans an OpenAPI reference it cannot hold: {report.Output}");

            var ran = new List<TextEdits.Command>();
            var applied = Invoke(root, Recording(ran), features, "--apply");

            expect(applied.Code == 0, $"A fresh {features} without the API was refused: {applied.Output}{applied.Error}");
            expect(Same(original, Snapshot(root)), $"After removing {features} without the API the tree is not the one that was there before it was generated: {Differences(original, Snapshot(root))}");
            expect(
                Launched(ran).SequenceEqual(["dotnet run Tools/Tasks.cs migration-check", "dotnet run Tools/Tasks.cs build"]),
                $"Removing {features} without the API ran more than the check and the build: {string.Join(" | ", Launched(ran))}");
        }
    }
}

internal static class TextEdits
{
    private static readonly UTF8Encoding _strictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    internal sealed record Edit(string Path, string Anchor, string Position, string[] Lines, string[] Replaced);

    internal sealed record Command(string Display, string WorkingDirectory, string FileName, string[] Arguments)
    {
        internal bool FromLastBuild => Arguments.Contains(_noBuild, StringComparer.Ordinal);
    }

    private const string _noBuild = "--no-build";

    internal sealed class Sheet
    {
        private readonly bool _bom;

        private readonly string _newline;

        private readonly List<string> _lines;

        private readonly string[] _source;

        private readonly List<int> _origin;

        private readonly bool _sourceFinalNewline;

        internal Sheet(string path, byte[] original)
        {
            Path = path;
            Original = original;
            _bom = original.AsSpan().StartsWith(Encoding.UTF8.Preamble);

            string text = _strictUtf8.GetString(original.AsSpan(_bom ? 3 : 0));

            _newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            FinalNewline = text.EndsWith('\n');
            _lines = [.. (FinalNewline ? text[..^_newline.Length] : text).Split(_newline)];
            _source = [.. _lines];
            _origin = [.. Enumerable.Range(0, _lines.Count)];
            _sourceFinalNewline = FinalNewline;
        }

        internal string Path { get; }

        internal byte[] Original { get; }

        internal IReadOnlyList<string> Lines => _lines;

        internal bool FinalNewline { get; private set; }

        internal bool EndingChanged => FinalNewline != _sourceFinalNewline;

        internal string Text => string.Join('\n', _lines) + (FinalNewline ? "\n" : string.Empty);

        internal void Rewrite(string text)
        {
            bool finalNewline = text.EndsWith('\n');
            string[] next = (finalNewline ? text[..^1] : text).Split('\n');
            int head = 0;

            while (head < next.Length && head < _lines.Count && next[head] == _lines[head])
            {
                head++;
            }

            int tail = 0;

            while (tail < next.Length - head && tail < _lines.Count - head && next[^(tail + 1)] == _lines[^(tail + 1)])
            {
                tail++;
            }

            int removed = _lines.Count - head - tail;

            _lines.RemoveRange(head, removed);
            _origin.RemoveRange(head, removed);
            Insert(head, next[head..^tail]);
            FinalNewline = finalNewline;
        }

        internal void Insert(int index, string[] lines)
        {
            _lines.InsertRange(index, lines);
            _origin.InsertRange(index, lines.Select(_ => -1));
        }

        internal IEnumerable<Edit> Edits()
        {
            int previous = -1;
            int previousSource = -1;

            for (int index = 0; index <= _lines.Count; index++)
            {
                if (index < _lines.Count && _origin[index] < 0)
                {
                    continue;
                }

                int nextSource = index < _lines.Count ? _origin[index] : _source.Length;
                string[] added = [.. _lines.Skip(previous + 1).Take(index - previous - 1)];
                string[] replaced = _source[(previousSource + 1)..nextSource];

                if (added.Length > 0 || replaced.Length > 0)
                {
                    int next = index < _lines.Count ? index : -1;
                    bool before = next >= 0 && (_lines[next].Trim().Length > 0 || previous < 0 || _lines[previous].Trim().Length == 0);

                    yield return new Edit(Path, _lines[before ? next : previous], before ? "before" : "after", added, replaced);
                }

                previous = index;
                previousSource = nextSource;
            }
        }

        internal byte[] Bytes()
        {
            byte[] text = Encoding.UTF8.GetBytes(string.Join(_newline, Lines) + (FinalNewline ? _newline : string.Empty));

            return _bom ? [.. Encoding.UTF8.Preamble, .. text] : text;
        }
    }

    internal sealed class Editor(string root)
    {
        private readonly Dictionary<string, Sheet?> _sheets = new(StringComparer.Ordinal);

        internal string Step { get; set; } = string.Empty;

        internal IReadOnlyList<Edit> Applied =>
            _sheets.Values.OfType<Sheet>().Any(sheet => sheet.EndingChanged)
                ? throw new InvalidOperationException("An edit that adds or removes the newline that ends a file cannot be recorded to be undone.")
                : [.. _sheets.Values.OfType<Sheet>().SelectMany(sheet => sheet.Edits())];

        internal List<string> Kept { get; } = [];

        internal List<string> Failures { get; } = [];

        internal IEnumerable<Sheet> Changed =>
            _sheets.Values.OfType<Sheet>().Where(sheet => !sheet.Original.AsSpan().SequenceEqual(sheet.Bytes()));

        internal Sheet? Open(string path)
        {
            if (!_sheets.TryGetValue(path, out Sheet? sheet))
            {
                string full = System.IO.Path.Combine(root, path);

                sheet = File.Exists(full) ? new Sheet(path, File.ReadAllBytes(full)) : null;
                _sheets[path] = sheet;
            }

            if (sheet is null)
            {
                Fail($"{path} does not exist.");
            }

            return sheet;
        }

        internal void Fail(string reason) => Failures.Add(Step.Length == 0 ? reason : $"{Step}: {reason}");

        internal static void Insert(Sheet sheet, int anchor, bool before, string[] lines) => sheet.Insert(before ? anchor : anchor + 1, lines);

        internal static int Find(Sheet sheet, Func<string, bool> match, int from = 0, int to = -1)
        {
            int end = to < 0 ? sheet.Lines.Count : Math.Min(to, sheet.Lines.Count);

            for (int index = Math.Max(from, 0); index < end; index++)
            {
                if (match(sheet.Lines[index]))
                {
                    return index;
                }
            }

            return -1;
        }

        internal static int FindLast(Sheet sheet, Func<string, bool> match, int from)
        {
            for (int index = Math.Min(from, sheet.Lines.Count - 1); index >= 0; index--)
            {
                if (match(sheet.Lines[index]))
                {
                    return index;
                }
            }

            return -1;
        }

        internal void Place(string path, string[] anchors, bool before, string[] lines, string present)
        {
            if (Open(path) is not { } sheet)
            {
                return;
            }

            if (Find(sheet, line => line.Trim().StartsWith(present, StringComparison.Ordinal)) >= 0)
            {
                Kept.Add(Step);

                return;
            }

            int anchor = anchors
                .Select(text => Find(sheet, line => line.Trim().StartsWith(text, StringComparison.Ordinal)))
                .FirstOrDefault(index => index >= 0, -1);

            if (anchor < 0)
            {
                Fail($"no line starts with '{string.Join("' or '", anchors)}', the line the edit is placed beside.");

                return;
            }

            Insert(sheet, anchor, before, lines);
        }

        internal void Rewrite(string path, Func<string, string?> edit)
        {
            if (Open(path) is not { } sheet)
            {
                return;
            }

            if (edit(sheet.Text) is { } text)
            {
                sheet.Rewrite(text);
            }
            else
            {
                Fail($"its anchor is not in {path}, so nothing could be written.");
            }
        }

        internal void PlaceInEachMethod(string path, string anchor, bool before, string line)
        {
            if (Open(path) is not { } sheet)
            {
                return;
            }

            int found = 0;

            for (int index = 0; index < sheet.Lines.Count; index++)
            {
                if (sheet.Lines[index].Trim() != anchor)
                {
                    continue;
                }

                found++;

                int start = FindLast(sheet, candidate => candidate == "    {", index);
                int end = Find(sheet, candidate => candidate == "    }", index);

                if (Find(sheet, candidate => candidate.Trim() == line.Trim(), start, end) >= 0)
                {
                    Kept.Add(Step);

                    continue;
                }

                Insert(sheet, index, before, [line]);
                index++;
            }

            if (found == 0)
            {
                Fail($"no line reads '{anchor}', the line the edit is placed beside in each method.");
            }
        }

        internal void AppendToMethod(string path, string method, string prefix, string line)
        {
            if (Open(path) is not { } sheet)
            {
                return;
            }

            int declaration = Find(sheet, candidate => candidate.Contains(method, StringComparison.Ordinal));
            int start = declaration < 0 ? -1 : Find(sheet, candidate => candidate == "    {", declaration);
            int end = start < 0 ? -1 : Find(sheet, candidate => candidate == "    }", start);

            if (end < 0)
            {
                Fail($"no method{method} with a body was found to take the line.");

                return;
            }

            if (Find(sheet, candidate => candidate.Trim() == line.Trim(), start, end) >= 0)
            {
                Kept.Add(Step);

                return;
            }

            int last = FindLast(sheet, candidate => candidate.Trim().StartsWith(prefix, StringComparison.Ordinal), end);

            if (last <= start)
            {
                Fail($"the method{method} has no line starting with '{prefix}' to place the line after.");

                return;
            }

            Insert(sheet, last, before: false, [line]);
        }

        internal void Using(string path, string space)
        {
            if (Open(path) is not { } sheet)
            {
                return;
            }

            string directive = $"using {space};";
            int body = Find(sheet, line => !IsHeader(line));
            int[] usings = [.. Enumerable.Range(0, body < 0 ? sheet.Lines.Count : body).Where(index => IsUsing(sheet.Lines[index]))];
            string? own = sheet.Lines
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.StartsWith("namespace ", StringComparison.Ordinal) && line.EndsWith(';'))?["namespace ".Length..^1];

            if (usings.Any(index => sheet.Lines[index].Trim() == directive)
                || (own is not null && (own == space || own.StartsWith(space + ".", StringComparison.Ordinal))))
            {
                return;
            }

            int following = usings.FirstOrDefault(index => Order(space, sheet.Lines[index].Trim()["using ".Length..].TrimEnd(';')) < 0, -1);

            if (following >= 0)
            {
                Insert(sheet, following, before: true, [directive]);
            }
            else if (usings.Length > 0)
            {
                Insert(sheet, usings[^1], before: false, [directive]);
            }
            else
            {
                Insert(sheet, 0, before: true, [directive, string.Empty]);
            }
        }

        internal void Row(string path, string heading, string row, string present)
        {
            if (Open(path) is not { } sheet)
            {
                return;
            }

            if (Find(sheet, line => line.Contains(present, StringComparison.Ordinal)) >= 0)
            {
                Kept.Add(Step);

                return;
            }

            int level = heading.TakeWhile(character => character == '#').Count();
            int section = Find(sheet, line => line == heading);
            int next = section < 0 ? -1 : Find(sheet, line => line.StartsWith('#') && line.TakeWhile(character => character == '#').Count() <= level, section + 1);
            int rule = section < 0 ? -1 : Find(sheet, line => line.StartsWith("|---", StringComparison.Ordinal), section + 1, next);

            if (rule < 0)
            {
                Fail($"{path} has no table under '{heading}' to take the row.");

                return;
            }

            int last = rule;

            while (last + 1 < sheet.Lines.Count && sheet.Lines[last + 1].StartsWith('|'))
            {
                last++;
            }

            Insert(sheet, last, before: false, [row]);
        }

        internal void Section(string path, string heading, string[] lines, string present)
        {
            if (Open(path) is not { } sheet)
            {
                return;
            }

            if (Find(sheet, line => line == present) >= 0)
            {
                Kept.Add(Step);

                return;
            }

            int section = Find(sheet, line => line == heading);
            int next = section < 0 ? -1 : Find(sheet, line => line.StartsWith("## ", StringComparison.Ordinal), section + 1);

            if (next < 0)
            {
                Fail($"{path} has no '{heading}' section followed by another, before which the section is placed.");

                return;
            }

            Insert(sheet, next, before: true, lines);
        }

        private static bool IsUsing(string line) =>
            line.StartsWith("using ", StringComparison.Ordinal) && line.TrimEnd().EndsWith(';') && !line.Contains('=', StringComparison.Ordinal);

        private static bool IsHeader(string line) =>
            line.Trim().Length == 0 || line.TrimStart().StartsWith("//", StringComparison.Ordinal) || IsUsing(line);

        private static int Order(string left, string right)
        {
            static bool IsSystem(string space) => space == "System" || space.StartsWith("System.", StringComparison.Ordinal);

            int system = IsSystem(right).CompareTo(IsSystem(left));

            return system != 0 ? system
                : StringComparer.OrdinalIgnoreCase.Compare(left, right) is var ignoringCase and not 0 ? ignoringCase
                : StringComparer.Ordinal.Compare(left, right);
        }
    }

    internal static void Commit(string root, IReadOnlyList<(string Path, byte[] Bytes)> created, IReadOnlyList<(string Path, byte[] Original, byte[] Bytes)> replaced, IReadOnlyList<string>? deleted = null)
    {
        var written = new List<string>();
        var directories = new List<string>();
        var overwritten = new List<(string Full, byte[] Original)>();
        var setAside = new List<(string Full, string Aside)>();
        string? staged = null;

        try
        {
            foreach (var (path, bytes) in created)
            {
                string full = System.IO.Path.Combine(root, path);
                var missing = new Stack<string>();

                for (string next = System.IO.Path.GetDirectoryName(full)!; !Directory.Exists(next); next = System.IO.Path.GetDirectoryName(next)!)
                {
                    missing.Push(next);
                }

                directories.AddRange(missing);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);

                using var stream = new FileStream(full, FileMode.CreateNew, FileAccess.Write);

                written.Add(full);
                stream.Write(bytes);
            }

            foreach (var (path, original, bytes) in replaced)
            {
                string full = System.IO.Path.Combine(root, path);

                staged = $"{full}.{Guid.NewGuid():N}.tmp";

                using (var stream = new FileStream(staged, FileMode.CreateNew, FileAccess.Write))
                {
                    stream.Write(bytes);
                }

                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(staged, File.GetUnixFileMode(full));
                }

                File.Move(staged, full, overwrite: true);
                staged = null;
                overwritten.Add((full, original));
            }

            foreach (string path in deleted ?? [])
            {
                string full = System.IO.Path.Combine(root, path);
                string aside = $"{full}.{Guid.NewGuid():N}.tmp";

                File.Move(full, aside);
                setAside.Add((full, aside));
            }
        }
        catch
        {
            foreach (var (full, aside) in Enumerable.Reverse(setAside))
            {
                File.Move(aside, full);
            }

            if (staged is not null && File.Exists(staged))
            {
                File.Delete(staged);
            }

            foreach (var (full, original) in overwritten)
            {
                File.WriteAllBytes(full, original);
            }

            foreach (string full in written.Where(File.Exists))
            {
                File.Delete(full);
            }

            foreach (string directory in Enumerable.Reverse(directories))
            {
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }

            throw;
        }

        foreach (var (_, aside) in setAside)
        {
            File.Delete(aside);
        }
    }

    internal static byte[] Encode(string path, string content)
    {
        byte[] text = Encoding.UTF8.GetBytes(content.ReplaceLineEndings("\n"));

        return path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".md", StringComparison.Ordinal)
            ? [.. Encoding.UTF8.GetPreamble(), .. text]
            : text;
    }

    internal static IEnumerable<(string Path, byte[] Original, byte[] Bytes)> Replacements(Editor editor) =>
        editor.Changed.Select(sheet => (sheet.Path, sheet.Original, sheet.Bytes()));

    internal static Command Task(string root, params string[] task) =>
        new($"dotnet run Tools/Tasks.cs {string.Join(' ', task)}", root, "dotnet", ["run", "Tools/Tasks.cs", .. task]);

    internal static Command FromLastBuild(string root, params string[] task) => Task(root, ["--", .. task, _noBuild]);

    internal static List<Command> ContractSteps(string root, string client, bool built) =>
    [
        built ? FromLastBuild(root, "openapi") : Task(root, "openapi"),
        .. Directory.Exists(System.IO.Path.Combine(root, "Src", "Clients", client)) ? [Task(root, "api-client")] : Array.Empty<Command>(),
        .. File.Exists(System.IO.Path.Combine(root, "Src", "Web", "packages", "api-client", "scripts", "generate.mjs"))
            ? [new Command($"cd Src/Web && pnpm api {_noBuild}", System.IO.Path.Combine(root, "Src", "Web"), OperatingSystem.IsWindows() ? "pnpm.cmd" : "pnpm", ["api", _noBuild])]
            : Array.Empty<Command>(),
    ];

    internal static int Execute(Command command)
    {
        var start = new System.Diagnostics.ProcessStartInfo(command.FileName) { WorkingDirectory = command.WorkingDirectory, UseShellExecute = false };

        foreach (string argument in command.Arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = System.Diagnostics.Process.Start(start)!;

            process.WaitForExit();

            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            Console.Error.WriteLine($"{command.FileName} could not be started: {exception.Message}");

            return 127;
        }
    }

    internal static bool RunAll(IReadOnlyList<Command> steps, Func<Command, int> run, TextWriter output, TextWriter error, string done)
    {
        for (int index = 0; index < steps.Count; index++)
        {
            output.WriteLine();
            output.WriteLine($"Running {steps[index].Display}");
            output.Flush();

            int code = run(steps[index]);

            if (code == 0)
            {
                continue;
            }

            error.WriteLine($"error: `{steps[index].Display}` failed with exit code {code}.");
            error.WriteLine($"Done: {done}{string.Concat(steps.Take(index).Select(step => $", then `{step.Display}`"))}.");
            error.WriteLine("What remains, in this order, once the failure is fixed:");

            foreach (Command remaining in steps.Skip(index))
            {
                error.WriteLine($"  {remaining.Display}");
            }

            if (steps.Skip(index).Any(remaining => remaining.FromLastBuild))
            {
                error.WriteLine(
                    $"A command with {_noBuild} works from the build this command made: when the fix changes the code, run "
                    + "`dotnet run Tools/Tasks.cs build` before it.");
            }

            return false;
        }

        output.WriteLine();
        output.WriteLine($"Done: {done}{string.Concat(steps.Select(step => $", then `{step.Display}`"))}.");

        return true;
    }
}

internal sealed class FeatureManifest
{
    internal const string Folder = ".feature-manifest";

    internal const int EditsVersion = 2;

    private readonly JsonObject _top;

    private readonly SortedDictionary<string, string> _files = new(StringComparer.Ordinal);

    private readonly List<TextEdits.Edit> _edits;

    private readonly int _editsRead;

    private FeatureManifest(string path, JsonObject top, byte[]? original, List<TextEdits.Edit> edits)
    {
        Path = path;
        Original = original;
        _top = top;
        _edits = edits;
        _editsRead = edits.Count;

        foreach (var file in ((JsonArray)top["files"]!).OfType<JsonObject>())
        {
            _files[file["path"]!.GetValue<string>()] = file["sha256"]!.GetValue<string>();
        }
    }

    internal string Path { get; }

    internal byte[]? Original { get; }

    internal int? SchemaVersion =>
        _top["schemaVersion"] is JsonValue value && value.GetValueKind() == JsonValueKind.Number && value.TryGetValue(out int version) ? version : null;

    internal string? Generator => Text(_top["generator"]);

    internal string? Feature => Text(_top["feature"]);

    internal string? Type => Text(_top["type"]);

    internal string? Page => Text(_top["page"]);

    internal JsonObject? Options => _top["options"] as JsonObject;

    internal JsonArray? Fields => _top["fields"] as JsonArray;

    internal IReadOnlyDictionary<string, string> Files => _files;

    internal IReadOnlyList<TextEdits.Edit> Edits => _edits;

    internal IReadOnlyList<(string Declaration, string Migration, string[] Paths)> AddedFields =>
        [
            .. (_top["addedFields"] as JsonArray ?? [])
                .OfType<JsonObject>()
                .Where(addition => Text(addition["declaration"]) is not null && Text(addition["migration"]) is not null)
                .Select(addition => (
                    Text(addition["declaration"])!,
                    Text(addition["migration"])!,
                    (string[])[.. (addition["files"] as JsonArray ?? []).OfType<JsonObject>().Select(file => Text(file["path"])).OfType<string>()])),
        ];

    internal (string Path, byte[] Original, byte[] Bytes) Replacement => (Path, Original!, Bytes());

    internal static string PathOf(string features) => $"{Folder}/{features}.json";

    internal static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    internal static FeatureManifest Create(string generator, string features, string type, JsonObject options, JsonArray? fields, string? page)
    {
        var top = new JsonObject
        {
            ["schemaVersion"] = EditsVersion,
            ["generator"] = generator,
            ["feature"] = features,
            ["type"] = type,
            ["options"] = options,
        };

        if (fields is not null)
        {
            top["fields"] = fields;
        }

        top["files"] = new JsonArray();

        if (page is not null)
        {
            top["page"] = page;
        }

        top["edits"] = new JsonArray();

        return new FeatureManifest(PathOf(features), top, null, []);
    }

    internal static FeatureManifest? Read(string root, string features, out string? problem)
    {
        string path = PathOf(features);
        string full = System.IO.Path.Combine(root, path);

        problem = null;

        if (!File.Exists(full))
        {
            return null;
        }

        byte[] original;

        try
        {
            original = File.ReadAllBytes(full);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            problem = $"cannot be read ({exception.Message})";

            return null;
        }

        try
        {
            if (JsonNode.Parse(original) is not JsonObject top
                || Text(top["feature"]) != features
                || top["files"] is not JsonArray files
                || files.Any(file => file is not JsonObject entry || Text(entry["path"]) is null || Text(entry["sha256"]) is null)
                || (top["edits"] is { } edits && edits is not JsonArray))
            {
                problem = $"does not read as the manifest of {features}";

                return null;
            }

            var read = new List<TextEdits.Edit>();

            foreach (var node in top["edits"] as JsonArray ?? [])
            {
                if (node is not JsonObject edit
                    || Text(edit["path"]) is not { } editPath
                    || Text(edit["anchor"]) is not { } anchor
                    || Text(edit["position"]) is not ("before" or "after") || edit["lines"] is not JsonArray lines
                    || lines.Any(line => Text(line) is null)
                    || (edit["replaced"] is { } replacedNode && (replacedNode is not JsonArray replacedLines || replacedLines.Any(line => Text(line) is null))))
                {
                    problem = $"does not read as the manifest of {features}: an edit names no file, anchor, position or lines";

                    return null;
                }

                read.Add(new TextEdits.Edit(
                    editPath,
                    anchor,
                    Text(edit["position"])!,
                    [.. lines.Select(line => Text(line)!)],
                    [.. (edit["replaced"] as JsonArray ?? []).Select(line => Text(line)!)]));
            }

            return new FeatureManifest(path, top, original, read);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            problem = $"cannot be read ({exception.Message})";

            return null;
        }
    }

    internal void Record(string path, byte[] bytes) => _files[path] = Sha256(bytes);

    internal void RecordAddition(string declaration, string migration, IEnumerable<(string Path, byte[]? Before, byte[] After, IReadOnlyList<string> Changes)> files)
    {
        var additions = _top["addedFields"] as JsonArray ?? [];

        additions.Add((JsonNode)new JsonObject
        {
            ["declaration"] = declaration,
            ["migration"] = migration,
            ["files"] = new JsonArray([.. files.Select(file => (JsonNode)new JsonObject
            {
                ["path"] = file.Path,
                ["before"] = file.Before is null ? null : Sha256(file.Before),
                ["after"] = Sha256(file.After),
                ["changes"] = new JsonArray([.. file.Changes.Select(change => (JsonNode)JsonValue.Create(change)!)]),
            })]),
        });
        _top["addedFields"] = additions;
    }

    internal void Record(TextEdits.Editor editor)
    {
        foreach (var sheet in editor.Changed)
        {
            if (_files.TryGetValue(sheet.Path, out string? recorded) && recorded == Sha256(sheet.Original))
            {
                _files[sheet.Path] = Sha256(sheet.Bytes());
            }
        }

        _edits.AddRange(editor.Applied.Where(edit => !_files.ContainsKey(edit.Path)));
    }

    internal byte[] Bytes()
    {
        var files = new JsonArray();

        foreach (var (path, sha256) in _files)
        {
            files.Add((JsonNode)new JsonObject { ["path"] = path, ["sha256"] = sha256 });
        }

        _top["files"] = files;

        var edits = new JsonArray();

        foreach (var edit in _edits)
        {
            var written = new JsonObject
            {
                ["path"] = edit.Path,
                ["anchor"] = edit.Anchor,
                ["position"] = edit.Position,
                ["lines"] = new JsonArray([.. edit.Lines.Select(line => (JsonNode)JsonValue.Create(line)!)]),
            };

            if (edit.Replaced.Length > 0)
            {
                written["replaced"] = new JsonArray([.. edit.Replaced.Select(line => (JsonNode)JsonValue.Create(line)!)]);
            }

            edits.Add((JsonNode)written);
        }

        if (_edits.Count > _editsRead)
        {
            _top["schemaVersion"] = Math.Max(SchemaVersion ?? 1, EditsVersion);
            _top.Remove("edits");
            _top["edits"] = edits;
        }
        else if (_top.ContainsKey("edits"))
        {
            _top["edits"] = edits;
        }

        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            _top.WriteTo(writer);
        }

        return Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(stream.ToArray()).ReplaceLineEndings("\n") + "\n");
    }

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;
}

#:property UseAppHost=false

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary>
/// Reports what removing a feature involves, and undoes a generated skeleton nobody has touched.
/// </summary>
/// <remarks>
/// <para>
/// <b>The default is a report and writes nothing.</b> It lists the files the feature owns, the lines
/// elsewhere that confirm a reference (a registration, an EF model configuration, a migration or the
/// snapshot, a use of one of its types), the lines that merely contain its name, and the checks to
/// run afterwards. It can be run again and again, on a dirty tree.
/// </para>
/// <para>
/// <b><c>--apply</c> deletes only what <c>NewFeature.cs</c> wrote.</b> The manifest that command left
/// in <c>.feature-manifest/</c> lists each file with its SHA-256. Every one must still match, the
/// feature's generated folders must hold nothing else, and nothing outside them may mention the
/// feature in a way the report calls confirmed. When any of that fails, nothing is deleted and the
/// report says why. A feature that was composed by hand, and any shipped example, has no manifest or
/// no longer passes, and is removed with the manual procedure.
/// </para>
/// <para>
/// A migration is never touched: whether its tables may be dropped depends on the databases that
/// applied it, which no file in the tree can say.
/// </para>
/// <example>
/// <code>
/// dotnet run Tools/RemoveFeature.cs Widgets
/// dotnet run Tools/RemoveFeature.cs Widgets --apply
/// dotnet run Tools/RemoveFeature.cs --self-test
/// </code>
/// </example>
/// </remarks>
internal static class RemoveFeature
{
    private const string ManifestFolder = ".feature-manifest";

    private const string Usage =
        "Usage: dotnet run Tools/RemoveFeature.cs <Features> [--apply] [--root <path>]\n"
        + "  <Features>  the folder of the feature, PascalCase and plural, e.g. Widgets\n"
        + "  --apply     delete the generated files of an untouched, uncomposed feature. Without it,\n"
        + "              the command only reports and writes nothing.\n"
        + "  --root      the tree to read, default the current directory.";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    private static readonly Regex PascalCase = new(@"^[A-Z][A-Za-z0-9]*$", RegexOptions.None, TimeSpan.FromSeconds(1));

    private static readonly HashSet<string> SkippedFolders = new(StringComparer.Ordinal)
    {
        ".git", ".vs", ".claude", "bin", "obj", "artifacts", "node_modules", "TestResults",
    };

    private static readonly string[] TextExtensions =
    [
        ".cs", ".csproj", ".props", ".targets", ".sln", ".slnx", ".json", ".http", ".md", ".yml", ".yaml",
        ".xml", ".config", ".txt", ".sh", ".editorconfig",
    ];

    private enum Kind
    {
        Composition,
        Model,
        Migration,
        Reference,
        Possible,
    }

    private sealed record Hit(Kind Kind, string Path, int Line, string Text);

    private sealed record Entry(string Path, string Sha256);

    private sealed record Manifest(string Type, List<Entry> Files);

    private sealed record Analysis(
        string Features,
        string? Type,
        bool HasManifest,
        List<string> Folders,
        List<string> Owned,
        List<string> Modified,
        List<string> Missing,
        List<string> Extra,
        List<Hit> Hits,
        List<string> Blockers);

    private static int Main(string[] args)
    {
        if (Array.IndexOf(args, "--self-test") >= 0)
        {
            return SelfTest();
        }

        if (args is ["--help"] or ["-h"])
        {
            Console.WriteLine(Usage);

            return 0;
        }

        return Run(args, Console.Out, Console.Error);
    }

    private static int Run(string[] args, TextWriter output, TextWriter error)
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

        if (features is null || !PascalCase.IsMatch(features))
        {
            return Refuse(error, "Name the feature folder, PascalCase and plural, e.g. Widgets.");
        }

        root = Path.GetFullPath(root);

        var analysis = Analyze(root, features);

        if (!analysis.HasManifest && analysis.Folders.Count == 0 && analysis.Hits.All(hit => hit.Kind == Kind.Possible))
        {
            output.WriteLine($"No feature folder called {features} is left under Src or Tests, and no manifest names it.");
            PrintHits(output, analysis);

            return 0;
        }

        if (!apply)
        {
            PrintReport(output, analysis);
            output.WriteLine();
            output.WriteLine(
                analysis.Blockers.Count == 0
                    ? $"Nothing was changed. This feature can be undone with: --apply {features}"
                    : "Nothing was changed. --apply would refuse, for the reasons listed under Blockers.");

            return 0;
        }

        if (analysis.Blockers.Count > 0)
        {
            PrintReport(output, analysis);
            error.WriteLine();
            error.WriteLine($"error: {features} was not removed, and nothing was deleted. Remove it by hand with the report above.");

            return 1;
        }

        return Delete(root, analysis, output, error);
    }

    private static int Refuse(TextWriter error, string problem)
    {
        error.WriteLine($"error: {problem}");
        error.WriteLine();
        error.WriteLine(Usage);

        return 1;
    }

    // ---- Analysis -------------------------------------------------------------------------------

    private static Analysis Analyze(string root, string features)
    {
        var blockers = new List<string>();
        string manifestPath = $"{ManifestFolder}/{features}.json";
        string manifestFull = Path.Combine(root, manifestPath);

        Manifest? manifest = null;

        if (File.Exists(manifestFull))
        {
            manifest = ReadManifest(manifestFull, features, blockers);
        }
        else
        {
            blockers.Add($"'{manifestPath}' does not exist: the generator did not write {features} here, or the manifest was deleted. Only a manifest says what is safe to delete.");
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
                else if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(full))), entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    modified.Add(entry.Path);
                }
            }

            owned.AddRange(manifest.Files.Select(entry => entry.Path));
        }

        var folders = manifest is not null
            ? FeatureFolders(owned, features)
            : DiscoverFolders(root, features);

        var listed = owned.ToHashSet(StringComparer.Ordinal);
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

        var hits = Scan(root, features, manifest?.Type, folders, manifestPath);

        if (modified.Count > 0)
        {
            blockers.Add($"{modified.Count} generated file(s) were edited since they were written.");
        }

        if (missing.Count > 0)
        {
            blockers.Add($"{missing.Count} file(s) the manifest lists are gone, so the state is not the one the generator left.");
        }

        if (extra.Count > 0)
        {
            blockers.Add($"{extra.Count} file(s) were added to the feature's generated folders.");
        }

        foreach (var group in hits.Where(hit => hit.Kind != Kind.Possible).GroupBy(hit => hit.Kind))
        {
            blockers.Add($"{group.Count()} confirmed {Describe(group.Key)} outside the feature's own files.");
        }

        if (features == "Auth")
        {
            blockers.Add("Auth is a module with its own projects, database context and host composition; it is never removed by this command.");
        }

        return new Analysis(features, manifest?.Type, manifest is not null, folders, owned, modified, missing, extra, hits, blockers);
    }

    private static string Describe(Kind kind) => kind switch
    {
        Kind.Composition => "registration(s)",
        Kind.Model => "EF model reference(s)",
        Kind.Migration => "migration or snapshot mention(s)",
        Kind.Reference => "use(s) of the feature's types or namespace",
        _ => "possible match(es)",
    };

    private static Manifest? ReadManifest(string path, string features, List<string> blockers)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var top = document.RootElement;

            if (top.GetProperty("schemaVersion").GetInt32() != 1)
            {
                blockers.Add("The manifest has a schema version this command does not read.");

                return null;
            }

            if (top.GetProperty("feature").GetString() != features)
            {
                blockers.Add($"The manifest names another feature than {features}.");

                return null;
            }

            string type = top.GetProperty("type").GetString() ?? string.Empty;
            var files = new List<Entry>();
            var pattern = new Regex($"^(Src|Tests)/(.+/)?Features/{features}/.+$", RegexOptions.None, Timeout);

            foreach (var file in top.GetProperty("files").EnumerateArray())
            {
                string entry = file.GetProperty("path").GetString() ?? string.Empty;
                string digest = file.GetProperty("sha256").GetString() ?? string.Empty;

                if (entry.Contains("..", StringComparison.Ordinal) || entry.Contains('\\', StringComparison.Ordinal) || !pattern.IsMatch(entry) || digest.Length != 64)
                {
                    blockers.Add($"The manifest lists '{entry}', which is not a file under a Features/{features} folder of Src or Tests.");

                    return null;
                }

                files.Add(new Entry(entry, digest));
            }

            if (type.Length == 0 || files.Count == 0)
            {
                blockers.Add("The manifest names no type or no file.");

                return null;
            }

            return new Manifest(type, files);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or IOException)
        {
            blockers.Add($"The manifest cannot be read: {exception.Message}");

            return null;
        }
    }

    /// <summary>The <c>.../Features/&lt;Features&gt;</c> folder of each listed file.</summary>
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

            foreach (string file in Walk(start, "*"))
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

    private static IEnumerable<string> Walk(string directory, string pattern)
    {
        foreach (string file in Directory.EnumerateFiles(directory, pattern))
        {
            yield return file;
        }

        foreach (string child in Directory.EnumerateDirectories(directory))
        {
            if (!SkippedFolders.Contains(Path.GetFileName(child)))
            {
                foreach (string file in Walk(child, pattern))
                {
                    yield return file;
                }
            }
        }
    }

    private static string Snake(string pascal) =>
        string.Concat(pascal.Select((character, index) =>
            char.IsUpper(character) && index > 0
                ? "_" + char.ToLowerInvariant(character)
                : char.ToLowerInvariant(character).ToString()));

    private static List<Hit> Scan(string root, string features, string? type, List<string> folders, string manifestPath)
    {
        var composition = new List<Regex>
        {
            Rx($@"\bAdd{features}(Feature)?\b"),
            Rx($@"AddFeature\(\s*""{features}"""),
            Rx($@"nameof\(\w+\.Add{features}\)"),
        };

        var model = new List<Regex> { Rx($@"\b{features}Schema\b") };
        var reference = new List<Regex> { Rx($@"\.Features\.{features}\b") };
        var migration = new List<Regex> { Rx($@"\.Features\.{features}\b"), Rx($@"""{Snake(features)}""") };
        Regex? typedRegistration = null;
        var possible = new List<Regex> { Rx($@"\b{features}\b") };

        if (type is not null)
        {
            model.Add(Rx($@"\b{type}Record(Configuration)?\b"));
            reference.Add(Rx($@"\bI?{type}[A-Z]\w*"));
            reference.Add(Rx($@"\b(Get|Create|Update|Delete){type}s?\w*"));
            migration.Add(Rx($@"\b{type}Record\b"));
            typedRegistration = Rx($@"\b(Try)?Add\w*\s*[<(].*\bI?{type}[A-Z]");
            possible.Add(Rx($@"\b{type}\b"));
        }

        var ownFolders = folders.Select(folder => folder + "/").ToArray();
        var hits = new List<Hit>();

        foreach (string file in Walk(root, "*"))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');

            if (relative == manifestPath
                || ownFolders.Any(folder => relative.StartsWith(folder, StringComparison.Ordinal))
                || !TextExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)
                || relative.StartsWith($"{ManifestFolder}/", StringComparison.Ordinal))
            {
                continue;
            }

            bool source = relative.EndsWith(".cs", StringComparison.Ordinal)
                && (relative.StartsWith("Src/", StringComparison.Ordinal) || relative.StartsWith("Tests/", StringComparison.Ordinal));
            bool inMigrations = source && relative.Split('/').Contains("Migrations", StringComparer.Ordinal);
            string[] lines = File.ReadAllLines(file);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                Kind? kind = null;

                if (inMigrations)
                {
                    kind = migration.Any(pattern => pattern.IsMatch(line)) ? Kind.Migration : null;
                }
                else if (source)
                {
                    kind = composition.Any(pattern => pattern.IsMatch(line)) || (typedRegistration?.IsMatch(line) ?? false) ? Kind.Composition
                        : model.Any(pattern => pattern.IsMatch(line)) ? Kind.Model
                        : reference.Any(pattern => pattern.IsMatch(line)) ? Kind.Reference
                        : null;
                }

                kind ??= possible.Any(pattern => pattern.IsMatch(line)) ? Kind.Possible : null;

                if (kind is not null)
                {
                    hits.Add(new Hit(kind.Value, relative, i + 1, line.Trim()));
                }
            }
        }

        return hits;
    }

    private static Regex Rx(string pattern) => new(pattern, RegexOptions.None, Timeout);

    // ---- Report ---------------------------------------------------------------------------------

    private static void PrintReport(TextWriter output, Analysis analysis)
    {
        output.WriteLine($"Feature {analysis.Features}" + (analysis.Type is null ? " (no manifest, so its type is unknown)" : $" (type {analysis.Type})"));
        output.WriteLine();

        if (analysis.HasManifest)
        {
            output.WriteLine($"Owned by the generator: {analysis.Owned.Count} file(s) in {analysis.Folders.Count} folder(s), "
                + $"{analysis.Modified.Count} edited, {analysis.Missing.Count} missing, {analysis.Extra.Count} added since.");
            PrintList(output, "Edited", analysis.Modified);
            PrintList(output, "Missing", analysis.Missing);
            PrintList(output, "Added", analysis.Extra);
        }
        else
        {
            output.WriteLine($"Feature folders found ({analysis.Folders.Count}), none of them recorded by a manifest:");

            foreach (string folder in analysis.Folders)
            {
                output.WriteLine($"  {folder}");
            }
        }

        output.WriteLine();
        PrintHits(output, analysis);

        output.WriteLine();
        output.WriteLine("Known relationships between the shipped features:");
        output.WriteLine("  Reminders consumes TodoLists operations, events and records; TodoLists and Files share tagging;");
        output.WriteLine("  Auth has its own projects and database context and is removed through its own procedure.");
        output.WriteLine();
        output.WriteLine(analysis.Blockers.Count == 0 ? "Blockers: none." : "Blockers:");

        foreach (string blocker in analysis.Blockers)
        {
            output.WriteLine($"  - {blocker}");
        }

        output.WriteLine();
        output.WriteLine("After a removal, run:");
        output.WriteLine("  dotnet build <the solution>                  0 warnings, 0 errors");
        output.WriteLine("  dotnet test Tests/Architecture/<Prefix>.Architecture.Tests");
        output.WriteLine("  dotnet format <the solution> --verify-no-changes");
        output.WriteLine("  git status                                   review what is left, including docs/ and *.http samples");
        output.WriteLine("  a migration that drops the feature's tables, if a database applied one; never rewrite an applied migration");
    }

    private static void PrintHits(TextWriter output, Analysis analysis)
    {
        foreach (var group in new[] { Kind.Composition, Kind.Model, Kind.Migration, Kind.Reference })
        {
            var hits = analysis.Hits.Where(hit => hit.Kind == group).ToList();

            output.WriteLine($"Confirmed {Describe(group)}: {hits.Count}");

            foreach (var hit in hits)
            {
                output.WriteLine($"  {hit.Path}:{hit.Line}  {hit.Text}");
            }
        }

        var possible = analysis.Hits.Where(hit => hit.Kind == Kind.Possible).ToList();

        output.WriteLine($"Possible matches, the name as text, to read and judge: {possible.Count}");

        foreach (var hit in possible)
        {
            output.WriteLine($"  {hit.Path}:{hit.Line}  {hit.Text}");
        }
    }

    private static void PrintList(TextWriter output, string title, List<string> items)
    {
        foreach (string item in items)
        {
            output.WriteLine($"  {title}: {item}");
        }
    }

    // ---- Deletion -------------------------------------------------------------------------------

    private static int Delete(string root, Analysis analysis, TextWriter output, TextWriter error)
    {
        try
        {
            foreach (string file in analysis.Owned)
            {
                File.Delete(Path.Combine(root, file));
            }

            foreach (string folder in analysis.Folders)
            {
                RemoveEmpty(Path.Combine(root, folder));
            }

            File.Delete(Path.Combine(root, ManifestFolder, $"{analysis.Features}.json"));
            RemoveEmpty(Path.Combine(root, ManifestFolder));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"error: the deletion stopped part way: {exception.Message}");
            error.WriteLine("git status shows what is left; the manifest is still there if the stop came before its turn.");

            return 1;
        }

        output.WriteLine($"Removed {analysis.Owned.Count} generated file(s) of {analysis.Features}, its manifest and the folders left empty.");
        PrintHits(output, analysis);
        output.WriteLine();
        output.WriteLine("Run now: dotnet build the solution, then the architecture tests, then dotnet format --verify-no-changes.");
        output.WriteLine("It was never composed here, so no migration was written for it; any list above that is not empty is yours to read.");

        return 0;
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

    // ---- Self-test ------------------------------------------------------------------------------

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
            GeneratorCases(scratch, Expect);
        }
        finally
        {
            if (Directory.Exists(scratch))
            {
                Directory.Delete(scratch, recursive: true);
            }
        }

        Console.WriteLine($"Self-test: {checks} check(s), {problems.Count} problem(s).");

        foreach (string problem in problems)
        {
            Console.Error.WriteLine($"  {problem}");
        }

        return problems.Count == 0 ? 0 : 1;
    }

    private static (int Code, string Output, string Error) Invoke(string root, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int code = Run([.. args, "--root", root], output, error);

        return (code, output.ToString(), error.ToString());
    }

    private static Dictionary<string, string> Snapshot(string root) =>
        Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(root, path).Replace('\\', '/'),
                path => File.Exists(path) ? Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) : "<directory>",
                StringComparer.Ordinal);

    private static bool Same(Dictionary<string, string> before, Dictionary<string, string> after) =>
        before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out string? hash) && hash == pair.Value);

    private static void Put(string root, string relative, string content)
    {
        string full = Path.Combine(root, relative);

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private static string Sha(string root, string relative) =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, relative))));

    private static readonly string[] SkeletonFiles =
    [
        "Src/Domain/Acme.Domain/Features/Widgets/Entities/Widget.cs",
        "Src/Application/Acme.Application/Features/Widgets/Services/WidgetService.cs",
        "Src/Application/Acme.Application/Features/Widgets/UseCases/Queries/GetWidget/GetWidgetQuery.cs",
        "Tests/Application/Acme.Application.UnitTests/Features/Widgets/WidgetServiceTests.cs",
    ];

    /// <summary>A tree with a skeleton and a manifest written the way the generator writes it, and a host to compose it in.</summary>
    private static string Skeleton(string scratch)
    {
        string root = Path.Combine(scratch, Guid.CreateVersion7().ToString("N"));

        foreach (string file in SkeletonFiles)
        {
            Put(root, file, "namespace Acme;\n// " + file + "\n");
        }

        Put(root, "Src/Presentation/Acme.Api/Program.cs", "builder.Services.AddTodoLists();\n");
        Put(root, "Src/Infrastructure/Acme.Infrastructure.Persistence/Common/Contexts/AppDbContext.cs", "public const string TodoListsSchema = \"todo_lists\";\n");
        Put(root, "Src/Infrastructure/Acme.Infrastructure.Persistence/Migrations/20240101_InitialCreate.cs", "schema: \"todo_lists\"\n");
        Put(root, "README.md", "A template.\n");

        var json = new StringBuilder("{\n  \"schemaVersion\": 1,\n  \"generator\": \"Tools/NewFeature.cs\",\n  \"feature\": \"Widgets\",\n  \"type\": \"Widget\",\n  \"files\": [\n");

        json.Append(string.Join(",\n", SkeletonFiles.Order(StringComparer.Ordinal).Select(file => $"    {{ \"path\": \"{file}\", \"sha256\": \"{Sha(root, file)}\" }}")));
        json.Append("\n  ]\n}\n");
        Put(root, ".feature-manifest/Widgets.json", json.ToString());

        return root;
    }

    private static void HandWrittenCases(string scratch, Action<bool, string> expect)
    {
        // An untouched skeleton: the report writes nothing, then --apply removes exactly its files.
        string root = Skeleton(scratch);
        var before = Snapshot(root);
        var report = Invoke(root, "Widgets");

        expect(report.Code == 0 && Same(before, Snapshot(root)), "The report changed the tree, or failed on a clean skeleton.");
        expect(report.Output.Contains("Blockers: none.", StringComparison.Ordinal), "The report named a blocker on a clean skeleton.");

        var applied = Invoke(root, "Widgets", "--apply");
        var after = Snapshot(root);

        expect(applied.Code == 0, $"An untouched skeleton was refused: {applied.Error}{applied.Output}");
        expect(SkeletonFiles.All(file => !File.Exists(Path.Combine(root, file))), "A generated file was left behind.");
        expect(!Directory.Exists(Path.Combine(root, ManifestFolder)), "The manifest or its folder was left behind.");
        expect(!after.Keys.Any(key => key.Contains("Widgets", StringComparison.Ordinal)), "A folder of the feature was left behind.");
        expect(File.Exists(Path.Combine(root, "README.md")) && File.Exists(Path.Combine(root, "Src/Presentation/Acme.Api/Program.cs")), "A file the feature did not own was deleted.");

        var again = Invoke(root, "Widgets", "--apply");

        expect(again.Code == 0 && again.Output.Contains("No feature folder", StringComparison.Ordinal), "A second --apply did not say that nothing is left.");

        // A refusal leaves every byte as it was.
        void Refused(string name, Action<string> change, string expected)
        {
            string tree = Skeleton(scratch);

            change(tree);

            var snapshot = Snapshot(tree);
            var result = Invoke(tree, "Widgets", "--apply");

            expect(result.Code == 1, $"{name}: --apply did not refuse.");
            expect(Same(snapshot, Snapshot(tree)), $"{name}: a refusal changed the tree.");
            expect(result.Output.Contains(expected, StringComparison.Ordinal), $"{name}: the report does not say '{expected}'.");
        }

        Refused("modified file", tree => File.AppendAllText(Path.Combine(tree, SkeletonFiles[1]), "// edited\n"), "edited since");
        Refused("missing file", tree => File.Delete(Path.Combine(tree, SkeletonFiles[0])), "are gone");
        Refused("extra file", tree => Put(tree, "Src/Domain/Acme.Domain/Features/Widgets/Entities/Extra.cs", "x\n"), "added to the feature's generated folders");
        Refused("extra file in a new subfolder", tree => Put(tree, "Tests/Application/Acme.Application.UnitTests/Features/Widgets/Sub/Extra.cs", "x\n"), "added to the feature's generated folders");
        Refused("outside reference", tree => Put(tree, "Tests/Other/Acme.Other.Tests/UsesIt.cs", "using Acme.Application.Features.Widgets.Services;\n"), "use(s) of the feature's types");
        Refused("outside use of a type", tree => Put(tree, "Src/Other/Uses.cs", "var s = new WidgetService();\n"), "use(s) of the feature's types");
        Refused("a migration mentions it", tree => Put(tree, "Src/Infrastructure/Acme.Infrastructure.Persistence/Migrations/AppDbContextModelSnapshot.cs", "b.ToTable(\"widgets\", \"widgets\");\nusing Acme.Persistence.Features.Widgets.Models;\n"), "migration or snapshot");
        Refused("a snapshot mentions its record", tree => Put(tree, "Src/Infrastructure/Acme.Infrastructure.Persistence/Migrations/AppDbContextModelSnapshot.cs", "modelBuilder.Entity(\"Acme.WidgetRecord\", b => { });\n"), "migration or snapshot");
        Refused("a manifest path leaves the feature", tree => Put(tree, ".feature-manifest/Widgets.json", File.ReadAllText(Path.Combine(tree, ".feature-manifest/Widgets.json")).Replace(SkeletonFiles[0], "Src/Domain/Acme.Domain/Features/Widgets/../../../../README.md", StringComparison.Ordinal)), "not a file under a Features/Widgets folder");

        // The six edits the generator prints, each by itself.
        string[] edits =
        [
            "Src/Infrastructure/Acme.Infrastructure.Persistence/Common/Contexts/AppDbContext.cs|public const string WidgetsSchema = \"widgets\";",
            "Src/Infrastructure/Acme.Infrastructure.Persistence/Common/Contexts/AppDbContext.cs|internal DbSet<WidgetRecord> Widgets => Set<WidgetRecord>();",
            "Src/Infrastructure/Acme.Infrastructure.Persistence/Common/Contexts/AppDbContext.cs|modelBuilder.ApplyConfiguration(new WidgetRecordConfiguration());",
            "Src/Application/Acme.Application/ApplicationModule.cs|services.AddWidgets();",
            "Src/Application/Acme.Application/ApplicationModule.cs|services.AddFeature(\"Widgets\");",
            "Src/Application/Acme.Application/ApplicationModule.cs|services.AddScoped<IWidgetService, WidgetService>();",
            "Src/Infrastructure/Acme.Infrastructure.Persistence/PersistenceModule.cs|AddWidgetsFeature(services);",
            "Src/Infrastructure/Acme.Infrastructure.Persistence/PersistenceModule.cs|services.TryAddScoped<IWidgetStore, WidgetStore>();",
            "Src/Presentation/Acme.Api/Program.cs|builder.Services.AddWidgets();",
            "Tests/Architecture/Acme.Architecture.Tests/Composition/HostComposition.cs|services.AddWidgets();",
            "Tests/Application/Acme.Application.UnitTests/ApplicationModuleTests.cs|nameof(ApplicationModule.AddWidgets),",
        ];

        foreach (string edit in edits)
        {
            string[] parts = edit.Split('|');
            string expected = parts[1].Contains("Schema", StringComparison.Ordinal) || parts[1].Contains("Record", StringComparison.Ordinal)
                ? "EF model"
                : "registration";

            Refused(
                $"composed ({parts[1]})",
                tree =>
                {
                    string full = Path.Combine(tree, parts[0]);

                    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                    File.AppendAllText(full, parts[1] + "\n");
                },
                $"confirmed {expected}");
        }

        // No manifest: the report reads the folders and --apply refuses.
        string bare = Skeleton(scratch);

        File.Delete(Path.Combine(bare, ManifestFolder, "Widgets.json"));

        var bareBefore = Snapshot(bare);
        var bareReport = Invoke(bare, "Widgets");
        var bareApply = Invoke(bare, "Widgets", "--apply");

        expect(bareReport.Code == 0 && bareReport.Output.Contains("no manifest", StringComparison.Ordinal), "A feature without a manifest was not reported as such.");
        expect(bareApply.Code == 1 && Same(bareBefore, Snapshot(bare)), "A feature without a manifest was deleted, or --apply did not refuse.");

        // A mention that is only text is listed, and does not stop the undo.
        string prose = Skeleton(scratch);

        Put(prose, "Sketches/Idea.md", "The Widgets feature is a sketch.\n");
        Put(prose, "Tools/Sample.cs", "var service = new WidgetService(); // Widgets\n");

        var proseApply = Invoke(prose, "Widgets", "--apply");

        expect(proseApply.Code == 0 && proseApply.Output.Contains("Sketches/Idea.md:1", StringComparison.Ordinal), "A text-only mention blocked the undo, or was not listed.");

        // A report on a dirty tree is still a report.
        string dirty = Skeleton(scratch);

        File.AppendAllText(Path.Combine(dirty, SkeletonFiles[0]), "// edited\n");

        var dirtySnapshot = Snapshot(dirty);
        var dirtyReport = Invoke(dirty, "Widgets");

        expect(dirtyReport.Code == 0 && Same(dirtySnapshot, Snapshot(dirty)) && dirtyReport.Output.Contains("Edited: " + SkeletonFiles[0], StringComparison.Ordinal), "The report on an edited skeleton changed it or missed the edit.");

        var auth = Invoke(Skeleton(scratch), "Auth", "--apply");

        expect(auth.Code == 0 || !auth.Output.Contains("Removed", StringComparison.Ordinal), "Auth was removed.");

        var badName = Invoke(Skeleton(scratch), "widgets", "--apply");

        expect(badName.Code == 1, "A name that is not PascalCase was accepted.");
    }

    private static string SourceFile([CallerFilePath] string path = "") => path;

    /// <summary>Runs the real generator, so the manifest read here is the one it writes and not a copy of its format.</summary>
    private static int Generate(string root, params string[] args)
    {
        string generator = Path.Combine(Path.GetDirectoryName(SourceFile())!, "NewFeature.cs");
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };

        foreach (string argument in (string[])["run", generator, .. args, "--root", root])
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;

        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();

        return process.ExitCode;
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
            string root = Path.Combine(scratch, Guid.CreateVersion7().ToString("N"));
            string features = combination[0];

            foreach (string directory in new[]
            {
                "Src/Domain/Acme.Domain", "Src/Application/Acme.Application", "Src/Infrastructure/Acme.Infrastructure.Persistence",
                "Src/Presentation/Acme.Api", "Tests/Application/Acme.Application.UnitTests", "Tests/Domain/Acme.Domain.UnitTests",
                "Tests/Infrastructure/Acme.Infrastructure.Persistence.UnitTests", "Tests/Presentation/Acme.Api.UnitTests",
            })
            {
                Directory.CreateDirectory(Path.Combine(root, directory, "Features"));
            }

            Put(root, "Src/Infrastructure/Acme.Infrastructure.Persistence/Common/Contexts/AppDbContext.cs", "// the context a person edits\n");
            Put(root, "Src/Presentation/Acme.Api/Program.cs", "builder.Services.AddTodoLists();\n");

            // The account-deleted reaction, as a tree generated with a feature ships it: this test is
            // about removing a feature, and the reaction a first owner feature would bring along is
            // the generator's own self-test's subject.
            Put(root, "Src/Application/Acme.Application.Core/Common/Ports/IDeletedAccountDataPolicy.cs", "// the reaction this tree already ships\n");

            var original = Snapshot(root);

            expect(Generate(root, combination) == 0, $"The generator refused {features}.");

            var generated = Snapshot(root);

            expect(generated.Count > original.Count, $"The generator wrote nothing for {features}.");

            var report = Invoke(root, features);

            expect(report.Code == 0 && Same(generated, Snapshot(root)) && report.Output.Contains("Blockers: none.", StringComparison.Ordinal), $"The report on a fresh {features} changed the tree or found a blocker: {report.Output}");

            // Composed by hand: refused, and nothing is deleted.
            File.AppendAllText(Path.Combine(root, "Src/Presentation/Acme.Api/Program.cs"), $"builder.Services.Add{features}();\n");

            var composedSnapshot = Snapshot(root);
            var composed = Invoke(root, features, "--apply");

            expect(composed.Code == 1 && Same(composedSnapshot, Snapshot(root)), $"{features} was removed although its registration was in Program.cs.");

            // Undone by hand, then applied: the tree is what it was before generation.
            Put(root, "Src/Presentation/Acme.Api/Program.cs", "builder.Services.AddTodoLists();\n");

            var applied = Invoke(root, features, "--apply");

            expect(applied.Code == 0, $"A fresh {features} was refused: {applied.Output}{applied.Error}");
            expect(Same(original, Snapshot(root)), $"After removing {features} the tree is not the one that was there before it was generated.");
        }
    }
}

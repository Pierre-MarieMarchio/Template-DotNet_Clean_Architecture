#:property UseAppHost=false

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class RemoveFeature
{
    private const string ManifestFolder = ".feature-manifest";

    private const string Usage =
        "Usage: dotnet run Tools/RemoveFeature.cs <Features> [--apply] [--root <path>]\n"
        + "  <Features>  the folder of the feature, PascalCase and plural, e.g. Widgets\n"
        + "  --apply     delete the generated files of an untouched, uncomposed feature. Without it,\n"
        + "              the command only reports and writes nothing.\n"
        + "  --root      the tree to read, default the current directory.\n"
        + "       dotnet run Tools/RemoveFeature.cs --self-test";

    private const int MaxPossiblePrinted = 40;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    private static readonly Regex Declaration = new(@"\b(?:(?:record\s+)?(?:class|struct)|interface|record|enum)\s+([A-Z]\w*)", RegexOptions.None, TimeSpan.FromSeconds(1));

    private static readonly Regex ProductPrefix = new(@"^(?:Src|Tests)/[^/]+/(.+?)\.(?:Domain|Application|Infrastructure|Presentation|Api)\b", RegexOptions.None, TimeSpan.FromSeconds(1));

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

    private sealed record Manifest(string Type, List<Entry> Files, List<string> Shared, string? Page);

    private sealed record Dependent(string Name, string How);

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
        List<string> Blockers,
        List<string> Shared,
        List<Dependent> Dependents,
        List<Hit> SharedUses,
        string? Page,
        bool PageGone);

    private static readonly Regex[] SharedPaths =
    [
        Rx(@"^Src/Application/[^/]+\.Application\.Core/Common/Ports/(IDeletedAccountDataPolicy|IUserAccountDirectory)\.cs$"),
        Rx(@"^Src/Application/[^/]+\.Application\.Core/Features/DeletedAccounts/UseCases/Commands/SweepDeletedAccountData/I?SweepDeletedAccountDataUseCase\.cs$"),
        Rx(@"^Src/Presentation/[^/]+/Features/BackgroundJobs/Jobs/DeletedAccountSweep(JobsExtensions|Options)\.cs$"),
        Rx(@"^Src/Presentation/[^/]+/Features/Auth/Composition/AccountUserAccountDirectory\.cs$"),
        Rx(@"^Src/Presentation/[^/]+/Features/ExternalIdentity/Composition/ExternalIdentityUserAccountDirectory\.cs$"),
    ];

    private static readonly Regex SharedFolder = Rx(@"^(Src/Application/[^/]+\.Application\.Core/Features/DeletedAccounts)/");

    private static readonly Regex SharedUse = Rx(
        @"\b(IDeletedAccountDataPolicy|IUserAccountDirectory|I?SweepDeletedAccountDataUseCase|AddDeletedAccountSweep(Jobs)?|DeletedAccountSweepOptions|AccountUserAccountDirectory|ExternalIdentityUserAccountDirectory)\b|\.Features\.DeletedAccounts\b");

    private static readonly HashSet<string> SharedHosts = new(StringComparer.Ordinal)
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

            if (analysis.Dependents.Count > 0)
            {
                error.WriteLine($"error: {features} holds the shared account-deletion files that {string.Join(", ", analysis.Dependents.Select(dependent => dependent.Name))} still depend on.");
            }

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

        var shared = manifest?.Shared ?? [];

        var folders = manifest is not null
            ? FeatureFolders([.. owned.Except(shared, StringComparer.Ordinal)], features)
            : DiscoverFolders(root, features);

        foreach (string path in shared)
        {
            var match = SharedFolder.Match(path);

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

        var declared = manifest is null ? [] : DeclaredTypes(root, manifest, manifest.Type);
        string? product = manifest?.Files.Select(entry => ProductPrefix.Match(entry.Path)).FirstOrDefault(match => match.Success)?.Groups[1].Value;
        var hits = Scan(root, features, manifest?.Type, declared, product, folders, listed, manifestPath);

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

        var dependents = new List<Dependent>();
        var uses = new List<Hit>();

        if (shared.Count > 0)
        {
            SharedDependents(root, features, listed, dependents, uses, blockers);
        }

        return new Analysis(features, manifest?.Type, manifest is not null, folders, owned, modified, missing, extra, hits, blockers, shared, dependents, uses, page, page is not null && !File.Exists(Path.Combine(root, page)));
    }

    private static void SharedDependents(string root, string features, HashSet<string> own, List<Dependent> dependents, List<Hit> uses, List<string> blockers)
    {
        string manifests = Path.Combine(root, ManifestFolder);

        if (Directory.Exists(manifests))
        {
            foreach (string file in Directory.EnumerateFiles(manifests, "*.json").Order(StringComparer.Ordinal))
            {
                string other = Path.GetFileNameWithoutExtension(file);

                if (other == features)
                {
                    continue;
                }

                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllBytes(file));

                    if (document.RootElement.GetProperty("options").GetProperty("access").GetString() == "owner")
                    {
                        dependents.Add(new Dependent(other, "generated, --access owner"));
                    }
                }
                catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or IOException)
                {
                    blockers.Add($"'{ManifestFolder}/{other}.json' cannot be read ({exception.Message}), so nothing can tell whether {other} depends on the account-deletion files this manifest brought.");
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

            foreach (string file in Walk(start, "*.cs"))
            {
                string relative = Path.GetRelativePath(root, file).Replace('\\', '/');

                if (own.Contains(relative))
                {
                    continue;
                }

                string[] lines = File.ReadAllLines(file);

                for (int i = 0; i < lines.Length; i++)
                {
                    string text = lines[i].Trim();

                    if (text.StartsWith("//", StringComparison.Ordinal) || !SharedUse.IsMatch(text))
                    {
                        continue;
                    }

                    int at = relative.IndexOf("/Features/", StringComparison.Ordinal);
                    string? folder = at < 0 ? null : relative[(at + "/Features/".Length)..].Split('/')[0];

                    if (folder is null || SharedHosts.Contains(folder))
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
                $"{uses.Count} line(s) still register the account-deletion files this manifest brought "
                + "(AddDeletedAccountSweep, AddDeletedAccountSweepJobs, the IUserAccountDirectory adapter, the architecture tests' double): undo them first.");
        }
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
            var shared = new List<string>();
            var pattern = new Regex($"^(Src|Tests)/(.+/)?Features/{features}/.+$", RegexOptions.None, Timeout);

            foreach (var file in top.GetProperty("files").EnumerateArray())
            {
                string entry = file.GetProperty("path").GetString() ?? string.Empty;
                string digest = file.GetProperty("sha256").GetString() ?? string.Empty;
                bool isShared = SharedPaths.Any(sharedPath => sharedPath.IsMatch(entry));

                if (entry.Contains("..", StringComparison.Ordinal) || entry.Contains('\\', StringComparison.Ordinal) || !(pattern.IsMatch(entry) || isShared) || digest.Length != 64)
                {
                    blockers.Add($"The manifest lists '{entry}', which is not a file under a Features/{features} folder of Src or Tests, nor one of the account-deletion files every owner feature shares.");

                    return null;
                }

                files.Add(new Entry(entry, digest));

                if (isShared)
                {
                    shared.Add(entry);
                }
            }

            if (type.Length == 0 || files.Count == 0)
            {
                blockers.Add("The manifest names no type or no file.");

                return null;
            }

            string? page = top.TryGetProperty("page", out var written) ? written.GetString() : null;

            if (page is not null && page != PagePath(features))
            {
                blockers.Add($"The manifest lists the page '{page}', where the generator writes '{PagePath(features)}'.");

                return null;
            }

            return new Manifest(type, files, shared, page);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or IOException)
        {
            blockers.Add($"The manifest cannot be read: {exception.Message}");

            return null;
        }
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

    private static string Snake(string pascal) => Separated(pascal, '_');

    private static string Kebab(string pascal) => Separated(pascal, '-');

    private static string Separated(string pascal, char separator) =>
        string.Concat(pascal.Select((character, index) =>
            char.IsUpper(character) && index > 0
                ? separator + char.ToLowerInvariant(character).ToString()
                : char.ToLowerInvariant(character).ToString()));

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

            foreach (Match match in Declaration.Matches(File.ReadAllText(full)))
            {
                names.Add(match.Groups[1].Value);
            }
        }

        names.Remove(type);

        return [.. names.Order(StringComparer.Ordinal)];
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

            if (c == '\'' )
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

    private static List<Hit> Scan(string root, string features, string? type, List<string> declared, string? product, List<string> folders, HashSet<string> ownFiles, string manifestPath)
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
        Regex? declaredName = null;

        if (type is not null)
        {
            model.Add(Rx($@"\b{type}Record(Configuration)?\b"));
            migration.Add(Rx($@"\b{type}Record\b"));
            possible.Add(Rx($@"\b{type}\b"));

            if (declared.Count > 0)
            {
                string names = string.Join("|", declared.Select(Regex.Escape));

                declaredName = Rx($@"\b(?:{names})\b");
                typedRegistration = Rx($@"\b(Try)?Add\w*\s*[<(].*\b(?:{names})\b");
                possible.Add(declaredName);
            }
        }

        var ownFolders = folders.Select(folder => folder + "/").ToArray();
        var hits = new List<Hit>();

        foreach (string file in Walk(root, "*"))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');

            if (relative == manifestPath
                || ownFiles.Contains(relative)
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
                string code = source ? Code(line) : line;
                string unnamed = product is null ? line : line.Replace(product, string.Empty, StringComparison.Ordinal);
                Kind? kind = null;

                if (inMigrations)
                {
                    kind = migration.Any(pattern => pattern.IsMatch(line)) ? Kind.Migration : null;
                }
                else if (source)
                {
                    kind = composition.Any(pattern => pattern.IsMatch(line)) || (typedRegistration?.IsMatch(code) ?? false) ? Kind.Composition
                        : model.Any(pattern => pattern.IsMatch(line)) ? Kind.Model
                        : reference.Any(pattern => pattern.IsMatch(line)) || (declaredName?.IsMatch(code) ?? false) ? Kind.Reference
                        : null;
                }

                kind ??= possible.Any(pattern => pattern.IsMatch(unnamed)) ? Kind.Possible : null;

                if (kind is not null)
                {
                    hits.Add(new Hit(kind.Value, relative, i + 1, line.Trim()));
                }
            }
        }

        return hits;
    }

    private static Regex Rx(string pattern) => new(pattern, RegexOptions.None, Timeout);

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

            if (analysis.Page is not null)
            {
                output.WriteLine();
                output.WriteLine(analysis.PageGone
                    ? $"Feature page: {analysis.Page} is already gone, which blocks nothing."
                    : $"Feature page: {analysis.Page}, written by the generator; --apply deletes it, edited or not.");
            }
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

        foreach (var hit in possible.Take(MaxPossiblePrinted))
        {
            output.WriteLine($"  {hit.Path}:{hit.Line}  {hit.Text}");
        }

        if (possible.Count > MaxPossiblePrinted)
        {
            output.WriteLine($"  ... and {possible.Count - MaxPossiblePrinted} more, not printed");
        }
    }

    private static void PrintList(TextWriter output, string title, List<string> items)
    {
        foreach (string item in items)
        {
            output.WriteLine($"  {title}: {item}");
        }
    }

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
                RemoveEmptyAncestors(root, folder);
            }

            foreach (string file in analysis.Shared)
            {
                RemoveEmptyAncestors(root, file);
            }

            if (analysis.Page is not null)
            {
                File.Delete(Path.Combine(root, analysis.Page));
                RemoveEmpty(Path.Combine(root, Path.GetDirectoryName(analysis.Page)!));
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

        if (analysis.Page is not null)
        {
            output.WriteLine(analysis.PageGone
                ? $"Its page, {analysis.Page}, was already gone."
                : $"Removed its page, {analysis.Page}.");
            output.WriteLine($"Remove its row from docs/README.md, and any link to {analysis.Page["docs/".Length..]} from the other pages.");
        }

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

    private static void RemoveEmptyAncestors(string root, string relative)
    {
        string? folder = Path.GetDirectoryName(relative)?.Replace('\\', '/');

        while (!string.IsNullOrEmpty(folder) && folder is not ("Src" or "Tests"))
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
            GeneratorCases(scratch, Expect);
            NameCollisionCases(scratch, Expect);
            SharedFilesCases(scratch, Expect);
            PageCases(scratch, Expect);
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
        "Src/Domain/Acme.Domain/Features/Widgets/Model/Entities/Widget.cs",
        "Src/Application/Acme.Application/Features/Widgets/Services/WidgetService.cs",
        "Src/Application/Acme.Application/Features/Widgets/UseCases/Queries/GetWidget/GetWidgetQuery.cs",
        "Tests/Application/Acme.Application.UnitTests/Features/Widgets/WidgetServiceTests.cs",
    ];

    private static string Declarations(string name) => name switch
    {
        "Widget" => "public sealed record Widget;\n",
        "WidgetService" => "public interface IWidgetService;\npublic interface IWidgetStore;\npublic sealed class WidgetService;\npublic sealed class WidgetStore;\n",
        "GetWidgetQuery" => "public sealed record GetWidgetQuery;\n",
        _ => "public sealed class WidgetServiceTests;\n",
    };

    private static string Skeleton(string scratch)
    {
        string root = Path.Combine(scratch, Guid.CreateVersion7().ToString("N"));

        foreach (string file in SkeletonFiles)
        {
            Put(root, file, "namespace Acme;\n// " + file + "\n" + Declarations(Path.GetFileNameWithoutExtension(file)));
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
        Refused("extra file", tree => Put(tree, "Src/Domain/Acme.Domain/Features/Widgets/Model/Entities/Extra.cs", "x\n"), "added to the feature's generated folders");
        Refused("extra file in a new subfolder", tree => Put(tree, "Tests/Application/Acme.Application.UnitTests/Features/Widgets/Sub/Extra.cs", "x\n"), "added to the feature's generated folders");
        Refused("outside reference", tree => Put(tree, "Tests/Other/Acme.Other.Tests/UsesIt.cs", "using Acme.Application.Features.Widgets.Services;\n"), "use(s) of the feature's types");
        Refused("outside use of a type", tree => Put(tree, "Src/Other/Uses.cs", "var s = new WidgetService();\n"), "use(s) of the feature's types");
        Refused("a migration mentions it", tree => Put(tree, "Src/Infrastructure/Acme.Infrastructure.Persistence/Migrations/AppDbContextModelSnapshot.cs", "b.ToTable(\"widgets\", \"widgets\");\nusing Acme.Persistence.Features.Widgets.Rows.Models;\n"), "migration or snapshot");
        Refused("a snapshot mentions its record", tree => Put(tree, "Src/Infrastructure/Acme.Infrastructure.Persistence/Migrations/AppDbContextModelSnapshot.cs", "modelBuilder.Entity(\"Acme.WidgetRecord\", b => { });\n"), "migration or snapshot");
        Refused("a manifest path leaves the feature", tree => Put(tree, ".feature-manifest/Widgets.json", File.ReadAllText(Path.Combine(tree, ".feature-manifest/Widgets.json")).Replace(SkeletonFiles[0], "Src/Domain/Acme.Domain/Features/Widgets/../../../../README.md", StringComparison.Ordinal)), "not a file under a Features/Widgets folder");

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
            "Tests/Application/Acme.Application.UnitTests/ApplicationModuleTests.cs|services.AddWidgets();",
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

        string bare = Skeleton(scratch);

        File.Delete(Path.Combine(bare, ManifestFolder, "Widgets.json"));

        var bareBefore = Snapshot(bare);
        var bareReport = Invoke(bare, "Widgets");
        var bareApply = Invoke(bare, "Widgets", "--apply");

        expect(bareReport.Code == 0 && bareReport.Output.Contains("no manifest", StringComparison.Ordinal), "A feature without a manifest was not reported as such.");
        expect(bareApply.Code == 1 && Same(bareBefore, Snapshot(bare)), "A feature without a manifest was deleted, or --apply did not refuse.");

        string prose = Skeleton(scratch);

        Put(prose, "Sketches/Idea.md", "The Widgets feature is a sketch.\n");
        Put(prose, "Tools/Sample.cs", "var service = new WidgetService(); // Widgets\n");

        var proseApply = Invoke(prose, "Widgets", "--apply");

        expect(proseApply.Code == 0 && proseApply.Output.Contains("Sketches/Idea.md:1", StringComparison.Ordinal), "A text-only mention blocked the undo, or was not listed.");

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

    private static void Projects(string root)
    {
        foreach (string project in new[]
        {
            "Src/Domain/Acme.Domain", "Src/Application/Acme.Application", "Src/Infrastructure/Acme.Infrastructure.Persistence",
            "Src/Presentation/Acme.Api", "Tests/Application/Acme.Application.UnitTests", "Tests/Domain/Acme.Domain.UnitTests",
            "Tests/Infrastructure/Acme.Infrastructure.Persistence.UnitTests", "Tests/Presentation/Acme.Api.UnitTests",
        })
        {
            Put(root, $"{project}/{Path.GetFileName(project)}.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
        }

        Put(root, "cleanarch.json", "{\n  \"name\": \"Acme\"\n}\n");
    }

    private static string BareTree(string scratch, string adapter = "Auth")
    {
        string root = Path.Combine(scratch, Guid.CreateVersion7().ToString("N"));

        Projects(root);

        Put(root, "Src/Infrastructure/Acme.Infrastructure.Persistence/Common/Contexts/AppDbContext.cs", "// the context a person edits\n");
        Put(root, "Src/Presentation/Acme.Api/Program.cs", "builder.Services.AddPurgeExpiredIdempotencyKeys();\n");
        Put(root, "Src/Application/Acme.Application.Core/Common/Ports/ICurrentUser.cs", "public interface ICurrentUser;\n");
        Put(root, "Src/Application/Acme.Application.Core/ApplicationCoreModule.cs", "services.AddPurgeExpiredIdempotencyKeys();\n");
        Put(root, "Src/Application/Acme.Application.Core/Common/Events/UserAccountDeleted.cs", "/// rather than trusting delivery, see <c>IDeletedAccountDataPolicy</c>;\npublic sealed record UserAccountDeleted;\n");
        Put(root, "Src/Presentation/Acme.Api/Features/BackgroundJobs/Jobs/BackgroundJobsModule.cs", "services.AddMaintenanceJobs(configuration);\n");
        Put(root, "Tests/Architecture/Acme.Architecture.Tests/Composition/HostComposition.cs", "services.AddPurgeExpiredIdempotencyKeys();\n");
        Put(
            root,
            adapter == "Auth"
                ? "Src/Presentation/Acme.Api/Features/Auth/Composition/AuthComposition.cs"
                : "Src/Presentation/Acme.Api/Features/ExternalIdentity/Composition/ExternalAuthenticationComposition.cs",
            "services.AddScoped<ICurrentUser, CurrentUser>();\n");

        return root;
    }

    private static readonly string[] SharedFiles =
    [
        "Src/Application/Acme.Application.Core/Common/Ports/IDeletedAccountDataPolicy.cs",
        "Src/Application/Acme.Application.Core/Common/Ports/IUserAccountDirectory.cs",
        "Src/Application/Acme.Application.Core/Features/DeletedAccounts/UseCases/Commands/SweepDeletedAccountData/ISweepDeletedAccountDataUseCase.cs",
        "Src/Application/Acme.Application.Core/Features/DeletedAccounts/UseCases/Commands/SweepDeletedAccountData/SweepDeletedAccountDataUseCase.cs",
        "Src/Presentation/Acme.Api/Features/BackgroundJobs/Jobs/DeletedAccountSweepJobsExtensions.cs",
        "Src/Presentation/Acme.Api/Features/BackgroundJobs/Jobs/DeletedAccountSweepOptions.cs",
        "Src/Presentation/Acme.Api/Features/Auth/Composition/AccountUserAccountDirectory.cs",
    ];

    private static void SharedFilesCases(string scratch, Action<bool, string> expect)
    {
        string[] widgets = ["Widgets", "Widget", "--access", "owner", "--deletion", "delete"];

        string alone = BareTree(scratch);
        var original = Snapshot(alone);

        expect(Generate(alone, widgets) == 0, "The generator refused Widgets in a bare tree.");
        expect(SharedFiles.All(file => File.Exists(Path.Combine(alone, file))), "The first owner feature did not bring the shared files.");

        var report = Invoke(alone, "Widgets");

        expect(report.Output.Contains("Blockers: none.", StringComparison.Ordinal), $"(a) the report on the only owner feature found a blocker: {report.Output}");
        expect(SharedFiles.All(file => report.Output.Contains(file, StringComparison.Ordinal)), "(a) the report does not list the shared files.");

        var applied = Invoke(alone, "Widgets", "--apply");

        expect(applied.Code == 0, $"(a) the only owner feature was refused: {applied.Output}{applied.Error}");
        expect(Same(original, Snapshot(alone)), "(a) after removing the only owner feature the tree is not the bare one, or a folder was left empty.");

        void Refused(string name, string tree, string features, params string[] expected)
        {
            var snapshot = Snapshot(tree);
            var result = Invoke(tree, features, "--apply");

            expect(result.Code == 1, $"{name}: --apply did not refuse.");
            expect(Same(snapshot, Snapshot(tree)), $"{name}: a refusal changed the tree.");

            foreach (string text in expected)
            {
                expect((result.Output + result.Error).Contains(text, StringComparison.Ordinal), $"{name}: the report does not say '{text}'.");
            }
        }

        string WithWidgets(string adapter = "Auth")
        {
            string tree = BareTree(scratch, adapter);

            expect(Generate(tree, widgets) == 0, "The generator refused Widgets in a bare tree.");

            return tree;
        }

        string two = BareTree(scratch);
        var bare = Snapshot(two);

        expect(Generate(two, widgets) == 0 && Generate(two, "Sprockets", "Sprocket", "--access", "owner", "--deletion", "anonymise") == 0, "(b) the generator refused a feature.");
        Refused("(b) a second owner feature", two, "Widgets", "Sprockets (generated, --access owner)", "error: Widgets holds the shared account-deletion files that Sprockets still depend on.");
        expect(Invoke(two, "Sprockets", "--apply").Code == 0, "(b) the second owner feature was refused.");
        expect(Invoke(two, "Widgets", "--apply").Code == 0, "(b) the first owner feature was refused once it was the only one.");
        expect(Same(bare, Snapshot(two)), "(b) after removing both owner features the tree is not the bare one.");

        string notes = BareTree(scratch);
        var beforeNotes = Snapshot(notes);

        expect(Generate(notes, widgets) == 0 && Generate(notes, "Notes", "Note", "--access", "shared") == 0, "(c) the generator refused a feature.");

        var withNotes = Snapshot(notes);

        expect(Invoke(notes, "Widgets", "--apply").Code == 0, "(c) a shared feature held the owner feature back.");
        expect(
            withNotes.Where(pair => pair.Key.Contains("/Features/Notes", StringComparison.Ordinal) || pair.Key == $"{ManifestFolder}/Notes.json")
                .All(pair => Snapshot(notes).TryGetValue(pair.Key, out string? hash) && hash == pair.Value),
            "(c) removing Widgets touched Notes.");
        expect(Invoke(notes, "Notes", "--apply").Code == 0, "(c) Notes was refused.");
        expect(Same(beforeNotes, Snapshot(notes)), "(c) after removing both features the tree is not the bare one.");

        string journal = WithWidgets();

        Put(
            journal,
            "Src/Application/Acme.Application/Features/Journal/Services/Consumers/DeleteJournalOnAccountDeletedConsumer.cs",
            "// Deletes a journal when its account is.\ninternal sealed class DeleteJournalOnAccountDeletedConsumer(IUserAccountDirectory accounts) : IDeletedAccountDataPolicy;\n");
        Refused("(d) a hand-made owner feature", journal, "Widgets", "Journal (no manifest)");

        string[] registrations =
        [
            "Src/Application/Acme.Application.Core/ApplicationCoreModule.cs|public static IServiceCollection AddDeletedAccountSweep(this IServiceCollection services)",
            "Src/Presentation/Acme.Api/Program.cs|builder.Services.AddDeletedAccountSweep();",
            "Src/Presentation/Acme.Api/Features/BackgroundJobs/Jobs/BackgroundJobsModule.cs|services.AddDeletedAccountSweepJobs(configuration);",
            "Src/Presentation/Acme.Api/Features/Auth/Composition/AuthComposition.cs|services.AddScoped<IUserAccountDirectory, AccountUserAccountDirectory>();",
            "Tests/Architecture/Acme.Architecture.Tests/Composition/HostComposition.cs|services.AddDeletedAccountSweep();",
            "Tests/Architecture/Acme.Architecture.Tests/Composition/HostComposition.cs|services.AddScoped<IUserAccountDirectory, ArchitectureTestUserAccountDirectory>();",
            "Tests/Architecture/Acme.Architecture.Tests/Composition/HostComposition.cs|internal sealed class ArchitectureTestUserAccountDirectory : IUserAccountDirectory",
        ];

        foreach (string registration in registrations)
        {
            string[] parts = registration.Split('|');
            string tree = WithWidgets();

            File.AppendAllText(Path.Combine(tree, parts[0]), parts[1] + "\n");
            Refused($"(e) {parts[1]}", tree, "Widgets", "still register", $"{parts[0]}:2");
        }

        string edited = WithWidgets();

        File.AppendAllText(Path.Combine(edited, SharedFiles[1]), "// edited\n");
        Refused("(f) a shared file edited", edited, "Widgets", "edited since");

        string added = WithWidgets();

        Put(added, "Src/Application/Acme.Application.Core/Features/DeletedAccounts/UseCases/Commands/Extra.cs", "x\n");
        Refused("(g) a file added beside the sweep", added, "Widgets", "added to the feature's generated folders");

        string external = BareTree(scratch, "ExternalIdentity");
        var beforeExternal = Snapshot(external);

        expect(Generate(external, widgets) == 0, "(h) the generator refused Widgets in an externally authenticated bare tree.");
        expect(File.Exists(Path.Combine(external, "Src/Presentation/Acme.Api/Features/ExternalIdentity/Composition/ExternalIdentityUserAccountDirectory.cs")), "(h) the external account directory was not written.");
        expect(Invoke(external, "Widgets", "--apply").Code == 0, "(h) the only owner feature of an externally authenticated tree was refused.");
        expect(Same(beforeExternal, Snapshot(external)), "(h) after removing it the tree is not the bare one.");

        string stale = WithWidgets();

        Put(stale, $"{ManifestFolder}/Gizmos.json", "{ \"schemaVersion\": 1, \"feature\": \"Gizmos\", \"type\": \"Gizmo\", \"options\": { \"access\": \"owner\" }, \"files\": [] }\n");
        Refused("(i) a stale owner manifest", stale, "Widgets", "Gizmos (generated, --access owner)");

        string unreadable = WithWidgets();

        Put(unreadable, $"{ManifestFolder}/Gizmos.json", "{ not json\n");
        Refused("an unreadable manifest", unreadable, "Widgets", "nothing can tell whether Gizmos depends");
    }

    private static string PagedTree(string scratch)
    {
        string root = Path.Combine(scratch, Guid.CreateVersion7().ToString("N"));
        string template = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(SourceFile())!)!, "docs", "_page-templates", "feature.md");

        Projects(root);
        Ground(root);
        Put(root, "docs/_page-templates/feature.md", File.ReadAllText(template));
        Put(root, "docs/README.md", "| [Widgets](features/widgets.md) | what a caller can do with it |\n");

        return root;
    }

    private static void PageCases(string scratch, Action<bool, string> expect)
    {
        string pagePath = PagePath("Widgets");
        string[] widgets = ["Widgets", "Widget", "--access", "shared"];

        string fresh = PagedTree(scratch);
        var original = Snapshot(fresh);

        expect(Generate(fresh, widgets) == 0 && File.Exists(Path.Combine(fresh, pagePath)), "The generator did not write the feature page.");

        var report = Invoke(fresh, "Widgets");

        expect(report.Output.Contains($"Feature page: {pagePath}, written by the generator", StringComparison.Ordinal), $"The report does not name the page: {report.Output}");
        expect(report.Output.Contains("Blockers: none.", StringComparison.Ordinal), $"The page was taken for a blocker: {report.Output}");
        expect(!report.Output.Contains($"{pagePath}:", StringComparison.Ordinal), "The page's own lines were listed as matches.");

        var applied = Invoke(fresh, "Widgets", "--apply");

        expect(applied.Code == 0, $"A feature with its page was refused: {applied.Output}{applied.Error}");
        expect(applied.Output.Contains("Remove its row from docs/README.md", StringComparison.Ordinal), "The index row to remove was not printed.");
        expect(Same(original, Snapshot(fresh)), "After removing the feature, its page or the folder of pages was left behind, or the template went with it.");

        string edited = PagedTree(scratch);

        expect(Generate(edited, widgets) == 0, "The generator refused Widgets for the edited page.");
        File.AppendAllText(Path.Combine(edited, pagePath), "\nWidgets are what the warehouse ships.\n");
        expect(Invoke(edited, "Widgets", "--apply").Code == 0 && !File.Exists(Path.Combine(edited, pagePath)), "An edited page held the removal back, or was left behind.");

        string gone = PagedTree(scratch);

        expect(Generate(gone, widgets) == 0, "The generator refused Widgets for the deleted page.");
        File.Delete(Path.Combine(gone, pagePath));

        var goneApplied = Invoke(gone, "Widgets", "--apply");

        expect(goneApplied.Code == 0 && goneApplied.Output.Contains("was already gone", StringComparison.Ordinal), $"A page the team had deleted failed the removal: {goneApplied.Output}{goneApplied.Error}");

        string own = PagedTree(scratch);

        Put(own, pagePath, "# Widgets, as the team wrote them\n");
        expect(Generate(own, widgets) == 0, "The generator refused Widgets beside a page the team wrote.");
        expect(Invoke(own, "Widgets", "--apply").Code == 0, "Widgets beside a page the team wrote was refused.");
        expect(File.Exists(Path.Combine(own, pagePath)), "A page the generator did not write was deleted.");

        string forged = PagedTree(scratch);

        expect(Generate(forged, widgets) == 0, "The generator refused Widgets for the forged page.");

        string manifest = Path.Combine(forged, ManifestFolder, "Widgets.json");

        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace(pagePath, "docs/README.md", StringComparison.Ordinal));

        var snapshot = Snapshot(forged);
        var refused = Invoke(forged, "Widgets", "--apply");

        expect(refused.Code == 1 && Same(snapshot, Snapshot(forged)), "A manifest naming another page than the feature's was not refused cleanly.");
        expect(refused.Output.Contains($"where the generator writes '{pagePath}'", StringComparison.Ordinal), "The refusal of a forged page does not say why.");
    }

    private static void Ground(string root)
    {
        Put(root, "Src/Infrastructure/Acme.Infrastructure.Persistence/Common/Contexts/AppDbContext.cs", "// the context a person edits\n");
        Put(root, "Src/Presentation/Acme.Api/Program.cs", "builder.Services.AddTodoLists();\n");
        Put(root, "Src/Application/Acme.Application.Core/Common/Ports/IDeletedAccountDataPolicy.cs", "// the reaction this tree already ships\n");
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
                string root = Path.Combine(scratch, Guid.CreateVersion7().ToString("N"));

                Projects(root);
                Ground(root);
                Put(root, "Src/Presentation/Acme.Api/Noise.cs", Noise);
                Put(root, "Tests/Presentation/Acme.Api.UnitTests/ControllerContractTests.cs", Noise);

                string label = $"{row[0]} ({model})";

                expect(Generate(root, row[0], row[1], "--access", "shared", "--model", model, "--concurrency", model == "aggregate" ? "etag" : "none") == 0, $"The generator refused {label}.");

                var report = Invoke(root, row[0]);

                expect(
                    report.Output.Contains("Confirmed use(s) of the feature's types or namespace: 0", StringComparison.Ordinal),
                    $"{label}: a word that merely starts like the type, or the type in a string or comment, was a confirmed use: {report.Output}");

                var applied = Invoke(root, row[0], "--apply");

                expect(applied.Code == 0, $"{label}: an untouched skeleton was refused: {applied.Output}{applied.Error}");
            }
        }

        string used = Path.Combine(scratch, Guid.CreateVersion7().ToString("N"));

        Projects(used);
        Ground(used);
        expect(Generate(used, "Notes", "Note", "--access", "shared") == 0, "The generator refused Notes for the use case.");
        Put(used, "Src/Application/Acme.Application/Features/Reminders/Services/ReminderService.cs", "public sealed class ReminderService(INoteService notes);\n");

        var injected = Invoke(used, "Notes", "--apply");

        expect(injected.Code == 1 && injected.Output.Contains("confirmed use(s)", StringComparison.Ordinal), $"An injected INoteService did not block the removal: {injected.Output}");

        string registered = Path.Combine(scratch, Guid.CreateVersion7().ToString("N"));

        Projects(registered);
        Ground(registered);
        expect(Generate(registered, "Widgets", "Widget", "--access", "owner", "--deletion", "delete") == 0, "The generator refused Widgets for the use case.");

        string consumer = Directory.EnumerateFiles(registered, "*OnAccountDeleted*.cs", SearchOption.AllDirectories).Select(Path.GetFileNameWithoutExtension).FirstOrDefault() ?? "none";

        Put(registered, "Src/Presentation/Acme.Api/Wiring.cs", $"services.AddScoped<{consumer}>();\n");

        var wired = Invoke(registered, "Widgets", "--apply");

        expect(consumer != "none" && wired.Code == 1 && wired.Output.Contains("confirmed registration(s)", StringComparison.Ordinal), $"A consumer registered elsewhere ({consumer}) did not block the removal: {wired.Output}");
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

            Projects(root);

            Put(root, "Src/Infrastructure/Acme.Infrastructure.Persistence/Common/Contexts/AppDbContext.cs", "// the context a person edits\n");
            Put(root, "Src/Presentation/Acme.Api/Program.cs", "builder.Services.AddTodoLists();\n");

            Put(root, "Src/Application/Acme.Application.Core/Common/Ports/IDeletedAccountDataPolicy.cs", "// the reaction this tree already ships\n");

            var original = Snapshot(root);

            expect(Generate(root, combination) == 0, $"The generator refused {features}.");

            var generated = Snapshot(root);

            expect(generated.Count > original.Count, $"The generator wrote nothing for {features}.");

            var report = Invoke(root, features);

            expect(report.Code == 0 && Same(generated, Snapshot(root)) && report.Output.Contains("Blockers: none.", StringComparison.Ordinal), $"The report on a fresh {features} changed the tree or found a blocker: {report.Output}");

            File.AppendAllText(Path.Combine(root, "Src/Presentation/Acme.Api/Program.cs"), $"builder.Services.Add{features}();\n");

            var composedSnapshot = Snapshot(root);
            var composed = Invoke(root, features, "--apply");

            expect(composed.Code == 1 && Same(composedSnapshot, Snapshot(root)), $"{features} was removed although its registration was in Program.cs.");

            Put(root, "Src/Presentation/Acme.Api/Program.cs", "builder.Services.AddTodoLists();\n");

            var applied = Invoke(root, features, "--apply");

            expect(applied.Code == 0, $"A fresh {features} was refused: {applied.Output}{applied.Error}");
            expect(Same(original, Snapshot(root)), $"After removing {features} the tree is not the one that was there before it was generated.");

            foreach (string empty in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).Where(folder => !Directory.EnumerateFileSystemEntries(folder).Any()))
            {
                expect(false, $"After removing {features}, its last feature, '{Path.GetRelativePath(root, empty)}' was left empty.");
            }
        }
    }
}

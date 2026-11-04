#:property UseAppHost=false

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

return UpgradeReport.Run(args);

internal static class UpgradeReport
{
    private const string _usage =
        """
        Usage: dotnet run Tools/UpgradeReport.cs [--root <path>] [--to <version>] [--source <nupkg or folder>]...
                                                  [--json <file>] [--keep <folder>]
               dotnet run Tools/UpgradeReport.cs --self-test

        Says what a newer version of the package that generated this project, the one cleanarch.json
        names, changes, and writes nothing in the project. It installs the package at the project's
        version and at the target version, each in a template hive of its own under the system's
        temporary folder, generates the project from each under the name and options cleanarch.json
        records, adds through their item templates the parts the generation options cannot express,
        in the order cleanarch.json records them, then compares the two generations with the
        project's files:

          take      the target version changed the file and the project did not: take the file
                    as the target version writes it
          merge     the target version changed it and so did the project: merge by hand, from
                    the diff between the two versions printed at the end
          added     the target version adds the file
          removed   the target version no longer writes the file
          own       the project's own file, in neither generation: not concerned

        Files bootstrap or a framework's CLI writes are not compared and are listed apart, with the
        command that writes them; an option the two versions do not share is a decision to take by
        hand, never mapped. Line endings and a leading BOM are not differences, nor is the order of
        a C# file's using directives, nor the GUIDs dotnet new draws for a solution's entries and
        for the guids a template declares, such as a UserSecretsId. The changelog entries between
        the two versions come from the target package's CHANGELOG.md.

          --root <path>     the project, the current directory when left out
          --to <version>    the target version; the latest stable version the sources hold when
                            left out, so a prerelease is the target only when named here
          --source <path>   a .nupkg of the package, or a folder holding some, read in place of
                            the NuGet sources; repeat it to read several. Without it, the
                            versions and the packages come from the NuGet sources the NuGet.config
                            files of the project and the machine name, and nothing else is fetched
          --json <file>     also write the report as JSON to <file>, outside the project
          --keep <folder>   keep the two generations, as <folder>/<version>, outside the project:
                            the files to take come from there, and git merge-file merges a file
                            three ways from there

        No step of a package runs: a package whose templates declare a post-action other than
        manual instructions is refused. Exit code 0 when the report is printed; 1 on a refusal or
        a failed step.
        """;

    private const string _projectFileName = "cleanarch.json";

    private const string _manualInstructions = "AC1156F7-BB77-4DB8-B28F-24EEBCCA1E5C";

    private const string _mainTemplate = "CleanArch.WebApi.CSharp";

    private const string _webAppItem = "CleanArch.WebApi.WebApp.TypeScript";

    private const string _webPackageItem = "CleanArch.WebApi.WebPackage.TypeScript";

    private const string _presentationItem = "CleanArch.WebApi.Presentation.CSharp";

    private const string _scaffoldDescriptor = "scaffold.json";

    private const string _joiningSection = "## Joining a project generated without one";

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(2);

    private static readonly HashSet<string> _skippedFolders = new(StringComparer.Ordinal)
    {
        ".git", ".vs", ".idea", ".claude", "bin", "obj", "node_modules", "TestResults", "artifacts",
    };

    private static readonly (string Framework, string Name)[] _generatedHosts =
    [
        ("api", "Api"), ("blazor-server", "Portal"), ("mcp", "Mcp"), ("avalonia", "Desktop"), ("wpf", "Desktop"), ("worker", "Worker"),
    ];

    private static readonly Regex _usingDirective = new(@"^(global )?using [A-Za-z_][^(;]*;$", RegexOptions.None, _timeout);

    private static readonly Regex _version = new(
        @"^(?<core>\d+\.\d+\.\d+)(?:-(?<pre>[0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$", RegexOptions.None, _timeout);

    private static readonly Regex _changelogHeading = new(
        @"^## \[?(?<version>\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)\]?", RegexOptions.None, _timeout);

    private static readonly Regex _solutionEntry = new(
        @"^Project\(""\{[^}]+\}""\)\s*=\s*""[^""]*"",\s*""(?<path>[^""]*)"",\s*""\{(?<id>[0-9A-Fa-f-]{36})\}""",
        RegexOptions.Multiline,
        _timeout);

    private static readonly Regex _guid = new(
        @"(?<![0-9A-Fa-f-])[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}(?![0-9A-Fa-f-])",
        RegexOptions.None,
        _timeout);

    private static readonly Regex _joinedPath = new(@"^- in `(?<path>[^`]+)`", RegexOptions.None, _timeout);

    private static readonly Regex _templateManifest = new(@"^content/(?:.+/)?\.template\.config/template\.json$", RegexOptions.None, _timeout);

    private static readonly JsonSerializerOptions _written = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        NewLine = "\n",
    };

    private static readonly (Regex Path, string Writer)[] _writtenByBootstrap =
    [
        (Rx(@"^Src/Infrastructure/[^/]+\.Infrastructure\.(?:Persistence|Auth)/Migrations/"),
            "the migrations: bootstrap's migration-initial creates them, migration-add <Name> adds to them"),
        (Rx(@"^docs/(?:.+/)?index\.md$"), "dotnet run Tools/Tasks.cs docs-index"),
        (Rx(@"^Tests/Integration/[^/]+\.Api\.IntegrationTests/OpenApi/v1\.json$"), "dotnet run Tools/Tasks.cs openapi"),
        (Rx(@"^Src/Clients/[^/]+\.Client/Generated/"), "dotnet run Tools/Tasks.cs api-client"),
        (Rx(@"^Src/Web/pnpm-lock\.yaml$"), "pnpm install, in Src/Web"),
        (Rx(@"^Src/Web/packages/api-client/(?:src/generated|openapi)/"), "pnpm api, in Src/Web"),
    ];

    private static readonly (Regex Path, string Editor)[] _editedByBootstrap =
    [
        (Rx(@"^\.config/dotnet-tools\.json$"), "bootstrap pins CleanArch.Cli in it"),
        (Rx(@"^Src/Web/apps/[^/]+/package\.json$"), "web-scaffold pins each dependency to the version installed"),
    ];

    internal static int Run(string[] arguments)
    {
        if (arguments is ["--help"] or ["-h"])
        {
            Console.WriteLine(_usage);
            return 0;
        }

        if (arguments is ["--self-test"])
        {
            return SelfTest();
        }

        try
        {
            Report report = Build(Options.Parse(arguments));
            Print(report, Console.Out);
            return 0;
        }
        catch (InvalidOperationException error)
        {
            Console.Out.Flush();
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private sealed record Options(string Root, string? To, IReadOnlyList<string> Sources, string? Json, string? Keep)
    {
        internal static Options Parse(string[] arguments)
        {
            string root = Directory.GetCurrentDirectory();
            string? to = null;
            string? json = null;
            string? keep = null;
            var sources = new List<string>();

            for (int index = 0; index < arguments.Length; index++)
            {
                string option = arguments[index];

                if (option is not ("--root" or "--to" or "--source" or "--json" or "--keep"))
                {
                    throw new InvalidOperationException($"'{option}' is not an option of upgrade-report." + Environment.NewLine + _usage);
                }

                if (index + 1 >= arguments.Length || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"'{option}' needs a value." + Environment.NewLine + _usage);
                }

                string value = arguments[++index];

                switch (option)
                {
                    case "--root":
                        root = value;
                        break;
                    case "--to":
                        to = value;
                        break;
                    case "--json":
                        json = value;
                        break;
                    case "--keep":
                        keep = value;
                        break;
                    default:
                        sources.Add(value);
                        break;
                }
            }

            return new Options(
                Path.GetFullPath(root), to, sources, json is null ? null : Path.GetFullPath(json), keep is null ? null : Path.GetFullPath(keep));
        }
    }

    private sealed record Provenance(
        string Package,
        string Version,
        string Name,
        IReadOnlyList<(string Key, JsonNode Value)> Options,
        WebRecord? Web,
        IReadOnlyList<(string Name, string Framework)> Presentation,
        JsonObject Json)
    {
        internal static Provenance Read(string root)
        {
            string path = Path.Combine(root, _projectFileName);

            if (!File.Exists(path))
            {
                throw new InvalidOperationException(
                    $"{root} holds no {_projectFileName}, so nothing says which package version, name and options generated it: "
                    + "the report regenerates the project from that record. A project generated before the package wrote the file "
                    + "compares by hand, as docs/guides/upgrading.md says.");
            }

            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject json)
            {
                throw new InvalidOperationException($"{path} holds no JSON object.");
            }

            string Text(JsonNode? node, string what) =>
                node?.GetValue<string>() is { Length: > 0 } value ? value : throw new InvalidOperationException($"{path} records no {what}.");

            WebRecord? web = json["web"] is JsonObject workspace
                ? new WebRecord(
                    Text(workspace["scope"], "web.scope"),
                    [.. (workspace["apps"] as JsonArray ?? []).OfType<JsonObject>().Select(app => new WebApp(
                        Text(app["name"], "web app name"),
                        Text(app["framework"], "web app framework"),
                        Text(app["audience"], "web app audience"),
                        Text(app["rendering"], "web app rendering"),
                        app["port"]?.GetValue<int>()))],
                    [.. (workspace["packages"] as JsonArray ?? []).Select(package => Text(package, "web package name"))])
                : null;

            return new Provenance(
                Text(json["template"]?["package"], "template.package"),
                Text(json["template"]?["version"], "template.version"),
                Text(json["name"], "name"),
                [.. (json["options"] as JsonObject ?? []).Select(option => (option.Key, option.Value ?? JsonValue.Create(string.Empty)!))],
                web,
                [.. (json["presentation"] as JsonArray ?? []).OfType<JsonObject>()
                    .Select(host => (Text(host["name"], "presentation name"), Text(host["framework"], "presentation framework")))],
                json);
        }
    }

    private sealed record WebRecord(string Scope, IReadOnlyList<WebApp> Apps, IReadOnlyList<string> Packages);

    private sealed record WebApp(string Name, string Framework, string Audience, string Rendering, int? Port = null);

    private sealed record Release(string Version, string? File);

    private sealed record Template(string Identity, string ShortName, JsonObject Symbols, JsonArray PostActions, IReadOnlyDictionary<string, string> LongNames)
    {
        internal IEnumerable<Parameter> Parameters() =>
            Symbols
                .Where(symbol => symbol.Value?["type"]?.GetValue<string>() == "parameter")
                .Select(symbol => new Parameter(
                    symbol.Key,
                    symbol.Value!["datatype"]?.GetValue<string>() ?? "text",
                    symbol.Value["defaultValue"]?.ToString(),
                    [.. (symbol.Value["choices"] as JsonArray ?? []).Select(choice => choice?["choice"]?.GetValue<string>() ?? string.Empty)]))
                .Where(parameter => parameter.Choices is not ["refused"]);

        internal bool Declares(string symbol) => Parameters().Any(parameter => parameter.Name == symbol);

        internal string Option(string symbol) => "--" + LongNames.GetValueOrDefault(symbol, symbol);
    }

    private sealed record Parameter(string Name, string Datatype, string? Default, string[] Choices);

    private sealed record PackageContent(
        string Version,
        IReadOnlyDictionary<string, Template> Templates,
        string? Changelog,
        string? Repository,
        IReadOnlySet<string> HeldGuids,
        bool DrawsGuids)
    {
        internal static PackageContent Read(string nupkg)
        {
            using ZipArchive archive = ZipFile.OpenRead(nupkg);
            string Entry(ZipArchiveEntry entry)
            {
                using var reader = new StreamReader(entry.Open());
                return reader.ReadToEnd();
            }

            ZipArchiveEntry nuspecEntry = archive.Entries.FirstOrDefault(entry => !entry.FullName.Contains('/') && entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"{nupkg} holds no .nuspec, so it is not a NuGet package.");
            XElement metadata = XDocument.Parse(Entry(nuspecEntry)).Root!.Elements().First(element => element.Name.LocalName == "metadata");
            string? Meta(string name) => metadata.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value;
            string? repository = metadata.Elements().FirstOrDefault(element => element.Name.LocalName == "repository")?.Attribute("url")?.Value ?? Meta("projectUrl");

            var templates = new Dictionary<string, Template>(StringComparer.Ordinal);
            var heldGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool drawsGuids = false;

            foreach (ZipArchiveEntry entry in archive.Entries.Where(entry => entry.FullName.StartsWith("content/", StringComparison.Ordinal) && entry.Length > 0))
            {
                heldGuids.UnionWith(_guid.Matches(Entry(entry)).Select(match => match.Value));
            }

            foreach (ZipArchiveEntry entry in archive.Entries.Where(entry => _templateManifest.IsMatch(entry.FullName)))
            {
                JsonObject manifest = JsonNode.Parse(Entry(entry))!.AsObject();
                drawsGuids |= manifest["guids"] is JsonArray { Count: > 0 };
                string hostPath = entry.FullName[..^"template.json".Length] + "dotnetcli.host.json";
                JsonObject? host = archive.GetEntry(hostPath) is { } hostEntry ? JsonNode.Parse(Entry(hostEntry))!.AsObject() : null;
                Dictionary<string, string> longNames = (host?["symbolInfo"] as JsonObject ?? [])
                    .Where(symbol => symbol.Value?["longName"]?.GetValue<string>() is { Length: > 0 })
                    .ToDictionary(symbol => symbol.Key, symbol => symbol.Value!["longName"]!.GetValue<string>(), StringComparer.Ordinal);
                string identity = manifest["identity"]?.GetValue<string>() ?? entry.FullName;
                string shortName = manifest["shortName"] switch
                {
                    JsonArray names => names[0]!.GetValue<string>(),
                    JsonNode name => name.GetValue<string>(),
                    _ => identity,
                };
                templates[identity] = new Template(
                    identity, shortName, manifest["symbols"] as JsonObject ?? [], manifest["postActions"] as JsonArray ?? [], longNames);
            }

            string? changelog = archive.GetEntry("CHANGELOG.md") is { } log ? Entry(log) : null;
            return new PackageContent(Meta("version") ?? string.Empty, templates, changelog, repository, heldGuids, drawsGuids);
        }

        internal Template Require(string identity, string version) =>
            Templates.GetValueOrDefault(identity)
            ?? throw new InvalidOperationException(
                $"Version {version} of the package holds no template of identity {identity}, so the project cannot be generated from it the way "
                + "cleanarch.json describes; compare by hand.");
    }

    private sealed record Replay(
        string Template,
        string Name,
        IReadOnlyList<(string Symbol, string Value)> Values,
        IReadOnlyList<(string Symbol, string Value)> ProjectOptions,
        string Folder);

    private sealed record Plan(IReadOnlyList<(string Symbol, string Value)> Generation, IReadOnlyList<Replay> Replays);

    private enum Kind
    {
        Take,
        Merge,
        Added,
        Removed,
    }

    private sealed record Finding(Kind Kind, string Path, string? Note, string? Older, string? Newer);

    private sealed record Comparison(
        List<Finding> Findings,
        SortedDictionary<string, List<string>> Bootstrap,
        List<string> Scaffolds,
        List<string> Own,
        int Unchanged,
        string? Provenance);

    private sealed record Report(
        string Name,
        string From,
        string To,
        IReadOnlyList<string> Decisions,
        Comparison Comparison,
        IReadOnlyList<string> Diffs,
        string Changelog,
        string? Json,
        string? Kept);

    private static Report Build(Options options)
    {
        string root = options.Root;
        Provenance project = Provenance.Read(root);

        if (File.Exists(Path.Combine(root, ".template.config", "template.json")))
        {
            throw new InvalidOperationException(
                $"{root} is the checkout the package is packed from, not a project generated from it: there is no generated project to compare.");
        }

        if (options.Json is { } json && IsInside(json, root))
        {
            throw new InvalidOperationException($"--json {json} is inside the project, and the report writes nothing there: name a file outside it.");
        }

        if (options.Keep is { } keep && (IsInside(keep, root) || Path.GetFullPath(keep) == Path.GetFullPath(root)))
        {
            throw new InvalidOperationException($"--keep {keep} is inside the project, and the report writes nothing there: name a folder outside it.");
        }

        IReadOnlyList<Release> catalog = options.Sources.Count > 0
            ? LocalCatalog(options.Sources, project.Package)
            : NuGetCatalog(root, project.Package);
        (Release from, Release to) = ChooseVersions(project.Version, options.To, catalog, project.Package);

        string scratch = Directory.CreateTempSubdirectory("upgrade-report-").FullName;

        try
        {
            (string olderTree, PackageContent older, List<string> olderNotes) = Generate(root, project, from, scratch);
            (string newerTree, PackageContent newer, List<string> newerNotes) = Generate(root, project, to, scratch);

            List<string> decisions = [.. OptionChanges(older, newer, project, to.Version), .. olderNotes, .. newerNotes];
            RequireTheProjectsPaths(olderTree, root, ProjectFiles(root), project.Name);

            IReadOnlySet<string>? heldGuids = older.DrawsGuids || newer.DrawsGuids
                ? new HashSet<string>(older.HeldGuids.Concat(newer.HeldGuids), StringComparer.OrdinalIgnoreCase)
                : null;
            Comparison comparison = Compare(olderTree, newerTree, root, ProjectFiles(root), project.Name, JoinedPaths(olderTree, newerTree, project), heldGuids);
            string[] diffs = [.. comparison.Findings
                .Where(finding => finding.Kind == Kind.Merge)
                .Select(finding => TemplateDiff(scratch, olderTree, newerTree, finding))];
            string changelog = newer.Changelog is { } text
                ? ChangelogBetween(text, from.Version, to.Version)
                : $"Version {to.Version} of the package carries no CHANGELOG.md; the repository it names holds the entries: {newer.Repository ?? "(the package names none)"}.";

            string? kept = null;

            if (options.Keep is { } folder)
            {
                kept = folder;

                foreach ((string tree, string version) in new[] { (olderTree, from.Version), (newerTree, to.Version) })
                {
                    string target = Path.Combine(folder, version);

                    if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
                    {
                        throw new InvalidOperationException($"--keep {folder} holds {version} already, which the report does not overwrite: name another folder, or empty it.");
                    }

                    CopyTree(tree, target);
                }
            }

            var report = new Report(project.Name, from.Version, to.Version, decisions, comparison, diffs, changelog, options.Json, kept);

            if (options.Json is { } file)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, ToJson(report).ToJsonString(_written) + "\n");
            }

            return report;
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
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

    private static List<Release> LocalCatalog(IReadOnlyList<string> sources, string package)
    {
        var releases = new List<Release>();

        foreach (string source in sources.Select(Path.GetFullPath))
        {
            string[] files = Directory.Exists(source)
                ? [.. Directory.EnumerateFiles(source, "*.nupkg", SearchOption.AllDirectories)]
                : File.Exists(source) ? [source] : throw new InvalidOperationException($"--source {source} is neither a .nupkg nor a folder.");

            foreach (string file in files)
            {
                (string id, string version) = Identify(file);

                if (string.Equals(id, package, StringComparison.OrdinalIgnoreCase))
                {
                    releases.Add(new Release(version, file));
                }
            }
        }

        return releases;
    }

    private static (string Id, string Version) Identify(string nupkg)
    {
        using ZipArchive archive = ZipFile.OpenRead(nupkg);
        ZipArchiveEntry nuspec = archive.Entries.FirstOrDefault(entry => !entry.FullName.Contains('/') && entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"{nupkg} holds no .nuspec, so it is not a NuGet package.");
        using var reader = new StreamReader(nuspec.Open());
        XElement metadata = XDocument.Parse(reader.ReadToEnd()).Root!.Elements().First(element => element.Name.LocalName == "metadata");
        string Meta(string name) => metadata.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value ?? string.Empty;

        return (Meta("id"), Meta("version"));
    }

    private static IReadOnlyList<Release> NuGetCatalog(string root, string package)
    {
        (int exit, string output, string errors) = Capture(root, "dotnet", "package", "search", package, "--exact-match", "--prerelease", "--format", "json");
        JsonNode? result = null;

        try
        {
            result = JsonNode.Parse(output);
        }
        catch (JsonException)
        {
            result = null;
        }

        if (exit != 0 || result is null)
        {
            throw new InvalidOperationException(
                $"'dotnet package search {package}' failed, so the versions of the package are unknown: {(output + errors).Trim()} "
                + "Check the NuGet sources, or name a package with --source.");
        }

        string[] problems = [.. (result["problems"] as JsonArray ?? []).Select(problem => problem?.ToJsonString() ?? string.Empty)];

        if (problems.Length > 0)
        {
            throw new InvalidOperationException(
                $"'dotnet package search {package}' could not read every NuGet source: {string.Join("; ", problems)}");
        }

        return [.. (result["searchResult"] as JsonArray ?? [])
            .SelectMany(source => source?["packages"] as JsonArray ?? [])
            .Where(found => string.Equals(found?["id"]?.GetValue<string>(), package, StringComparison.OrdinalIgnoreCase))
            .Select(found => new Release(found!["version"]!.GetValue<string>(), null))];
    }

    private static (Release From, Release To) ChooseVersions(string current, string? to, IReadOnlyList<Release> catalog, string package)
    {
        string held = catalog.Count == 0
            ? "no version"
            : string.Join(", ", catalog.Select(release => release.Version).Distinct(StringComparer.OrdinalIgnoreCase).Order(Comparer<string>.Create(CompareVersions)));
        Release? Find(string version) => catalog.FirstOrDefault(release => ParseVersion(release.Version) is not null && CompareVersions(release.Version, version) == 0);

        if (ParseVersion(current) is null)
        {
            throw new InvalidOperationException($"cleanarch.json records the version '{current}', which is not a version a package is published under.");
        }

        Release from = Find(current)
            ?? throw new InvalidOperationException(
                $"Version {current} of {package}, the one cleanarch.json records, is not found: the sources hold {held}. "
                + "--source names a copy of that package.");

        if (to is not null)
        {
            if (ParseVersion(to) is null)
            {
                throw new InvalidOperationException($"--to '{to}' is not a version, such as 2.1.0 or 2.1.0-rc.1.");
            }

            Release target = Find(to)
                ?? throw new InvalidOperationException($"Version {to} of {package} is not found: the sources hold {held}.");

            return CompareVersions(target.Version, current) > 0
                ? (from, target)
                : throw new InvalidOperationException($"--to {to} is not newer than {current}, the version cleanarch.json records.");
        }

        Release? latest = catalog
            .Where(release => ParseVersion(release.Version) is { Prerelease: null })
            .Where(release => CompareVersions(release.Version, current) > 0)
            .MaxBy(release => release.Version, Comparer<string>.Create(CompareVersions));

        return latest is not null
            ? (from, latest)
            : throw new InvalidOperationException(
                $"No stable version of {package} is newer than {current}: the sources hold {held}. --to names a prerelease.");
    }

    private static (int[] Core, string[]? Prerelease)? ParseVersion(string version)
    {
        Match match = _version.Match(version.Trim());

        return match.Success
            ? ([.. match.Groups["core"].Value.Split('.').Select(part => int.Parse(part, CultureInfo.InvariantCulture))],
                match.Groups["pre"].Success ? match.Groups["pre"].Value.Split('.') : null)
            : null;
    }

    private static int CompareVersions(string? left, string? right)
    {
        (int[] leftCore, string[]? leftPre) = ParseVersion(left ?? string.Empty) ?? (new[] { 0, 0, 0 }, null);
        (int[] rightCore, string[]? rightPre) = ParseVersion(right ?? string.Empty) ?? (new[] { 0, 0, 0 }, null);

        for (int part = 0; part < 3; part++)
        {
            if (leftCore[part] != rightCore[part])
            {
                return leftCore[part].CompareTo(rightCore[part]);
            }
        }

        if (leftPre is null || rightPre is null)
        {
            return (leftPre is null ? 1 : 0) - (rightPre is null ? 1 : 0);
        }

        for (int part = 0; part < Math.Min(leftPre.Length, rightPre.Length); part++)
        {
            bool leftNumber = int.TryParse(leftPre[part], NumberStyles.None, CultureInfo.InvariantCulture, out int leftValue);
            bool rightNumber = int.TryParse(rightPre[part], NumberStyles.None, CultureInfo.InvariantCulture, out int rightValue);
            int order = (leftNumber, rightNumber) switch
            {
                (true, true) => leftValue.CompareTo(rightValue),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(leftPre[part].ToLowerInvariant(), rightPre[part].ToLowerInvariant()),
            };

            if (order != 0)
            {
                return order;
            }
        }

        return leftPre.Length.CompareTo(rightPre.Length);
    }

    private static (string Tree, PackageContent Content, List<string> Notes) Generate(string root, Provenance project, Release release, string scratch)
    {
        string hive = Path.Combine(scratch, "hive-" + release.Version);
        string tree = Path.Combine(scratch, release.Version);
        string[] hiveArguments = ["--debug:custom-hive", hive, "--debug:disable-sdk-templates"];

        try
        {
            Step(root, "dotnet", ["new", "install", release.File ?? $"{project.Package}::{release.Version}", .. hiveArguments]);
        }
        catch (InvalidOperationException error)
        {
            throw new InvalidOperationException(
                $"{error.Message} Installing version {release.Version} of {project.Package} failed: the output above says why.");
        }

        string nupkg = release.File
            ?? Directory.EnumerateFiles(hive, "*.nupkg", SearchOption.AllDirectories)
                .FirstOrDefault(file => Path.GetFileName(file).StartsWith(project.Package + ".", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"dotnet new installed version {release.Version}, and its package is not found under {hive}.");
        PackageContent content = PackageContent.Read(nupkg);
        RefuseRunningPostActions(content, release.Version);

        Plan plan = PlanOf(project);
        var notes = new List<string>();
        Template main = content.Require(_mainTemplate, release.Version);
        Directory.CreateDirectory(tree);
        Step(scratch, "dotnet", ["new", main.ShortName, "-n", project.Name, "-o", tree, .. Arguments(main, plan.Generation, release.Version, notes), .. hiveArguments]);

        foreach (Replay replay in plan.Replays)
        {
            Template item = content.Require(replay.Template, release.Version);
            (string, string)[] state =
            [
                ("hasClient", Directory.Exists(Path.Combine(tree, "Src", "Clients")) && Directory.EnumerateDirectories(Path.Combine(tree, "Src", "Clients"), "*.Client").Any() ? "true" : "false"),
                ("hasApi", Directory.Exists(Path.Combine(tree, "Src", "Presentation")) && Directory.EnumerateDirectories(Path.Combine(tree, "Src", "Presentation"), "*.Api").Any() ? "true" : "false"),
                ("workspace", File.Exists(Path.Combine(tree, "Src", "Web", "package.json")) ? "false" : "true"),
            ];
            (string, string)[] values = [.. replay.Values, .. replay.ProjectOptions.Concat(state).Where(value => item.Declares(value.Item1))];
            Step(tree, "dotnet", ["new", item.ShortName, "-n", replay.Name, .. Arguments(item, values, release.Version, notes), .. hiveArguments]);
        }

        return (tree, content, notes);
    }

    private static void RefuseRunningPostActions(PackageContent content, string version)
    {
        string[] running = [.. content.Templates.Values
            .SelectMany(template => template.PostActions.OfType<JsonObject>().Select(action => (template.ShortName, Id: action["actionId"]?.GetValue<string>() ?? string.Empty)))
            .Where(action => !string.Equals(action.Id, _manualInstructions, StringComparison.OrdinalIgnoreCase))
            .Select(action => $"{action.ShortName} {action.Id}")];

        if (running.Length > 0)
        {
            throw new InvalidOperationException(
                $"Version {version} of the package declares post-actions dotnet new would run, beyond printing instructions: {string.Join(", ", running)}. "
                + "The report runs nothing from a package, so it stops here: compare the versions by hand.");
        }
    }

    private static Plan PlanOf(Provenance project)
    {
        string scope = Regex.Replace(project.Name.ToLowerInvariant(), @"[.\s_]+", "-", RegexOptions.None, _timeout);
        (string Symbol, string Value)[] recorded = [.. project.Options.Select(option => (option.Key, Value(option.Value)))];
        var generation = new List<(string, string)>(recorded);
        var replays = new List<Replay>();

        WebApp[] apps = [.. project.Web?.Apps ?? []];
        bool firstAppGenerated = apps.Length > 0 && project.Web!.Scope == scope;

        if (firstAppGenerated)
        {
            generation.AddRange([("web", apps[0].Framework), ("webName", apps[0].Name), ("webAudience", apps[0].Audience), ("webRendering", apps[0].Rendering)]);
        }

        foreach (WebApp app in apps.Skip(firstAppGenerated ? 1 : 0))
        {
            replays.Add(new Replay(
                _webAppItem,
                app.Name,
                [("framework", app.Framework), ("audience", app.Audience), ("rendering", app.Rendering), .. PortOf(app), ("scope", project.Web!.Scope), ("projectName", project.Name)],
                [.. recorded],
                "Src/Web/apps/" + app.Name));
        }

        foreach (string package in project.Web?.Packages ?? [])
        {
            replays.Add(new Replay(_webPackageItem, package, [("scope", project.Web!.Scope)], [.. recorded], "Src/Web/packages/" + package));
        }

        foreach ((string name, string framework) in project.Presentation)
        {
            if (_generatedHosts.Contains((framework, name)))
            {
                generation.Add(("presentation", framework));
            }
            else
            {
                replays.Add(new Replay(
                    _presentationItem,
                    name,
                    [("framework", framework), ("projectName", project.Name)],
                    [.. recorded],
                    "Src/Presentation/*." + name));
            }
        }

        return new Plan(generation, replays);

        static string Value(JsonNode node) => node.GetValueKind() switch
        {
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => node.ToString(),
        };
    }

    private static (string, string)[] PortOf(WebApp app) =>
        app.Port is { } port ? [("port", port.ToString(CultureInfo.InvariantCulture))] : [];

    private static string[] Arguments(Template template, IEnumerable<(string Symbol, string Value)> values, string version, List<string> notes)
    {
        var arguments = new List<string>();
        Dictionary<string, Parameter> parameters = template.Parameters().ToDictionary(parameter => parameter.Name, StringComparer.Ordinal);

        foreach ((string symbol, string value) in values)
        {
            if (!parameters.TryGetValue(symbol, out Parameter? parameter))
            {
                notes.Add($"Version {version}'s {template.ShortName} has no option {symbol}, which the project's record sets to {value}: "
                    + "that generation leaves it out, so decide by hand what takes its place.");
                continue;
            }

            if (parameter.Choices.Length > 0 && !parameter.Choices.Contains(value, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Version {version}'s {template.ShortName} does not offer {value} for {template.Option(symbol)} ({string.Join(", ", parameter.Choices)}), "
                    + "the value the project's record holds, so the project cannot be generated from it: decide by hand what replaces it, then compare by hand.");
            }

            arguments.Add(template.Option(symbol));
            arguments.Add(value);
        }

        return [.. arguments];
    }

    private static IEnumerable<string> OptionChanges(PackageContent older, PackageContent newer, Provenance project, string version)
    {
        HashSet<string> used = [_mainTemplate, .. PlanOf(project).Replays.Select(replay => replay.Template)];

        foreach (string identity in older.Templates.Keys.Union(newer.Templates.Keys).Order(StringComparer.Ordinal))
        {
            Template? before = older.Templates.GetValueOrDefault(identity);
            Template? after = newer.Templates.GetValueOrDefault(identity);

            if (before is null || after is null)
            {
                yield return $"{(after ?? before)!.ShortName}: version {version} {(after is null ? "no longer holds" : "adds")} this template.";
                continue;
            }

            if (!used.Contains(identity))
            {
                continue;
            }

            Dictionary<string, Parameter> was = before.Parameters().ToDictionary(parameter => parameter.Name, StringComparer.Ordinal);
            Dictionary<string, Parameter> @is = after.Parameters().ToDictionary(parameter => parameter.Name, StringComparer.Ordinal);
            string[] removed = [.. was.Keys.Except(@is.Keys).Order(StringComparer.Ordinal)];
            string[] added = [.. @is.Keys.Except(was.Keys).Order(StringComparer.Ordinal)];

            foreach (string name in removed)
            {
                string recorded = identity == _mainTemplate && project.Options.Any(option => option.Key == name) ? ", which cleanarch.json records" : string.Empty;
                yield return $"{after.ShortName}: version {version} removes the option {before.Option(name)}{recorded}"
                    + (added.Length > 0 ? $"; it adds {string.Join(", ", added.Select(after.Option))}, and a rename is never assumed" : string.Empty)
                    + ": decide by hand.";
            }

            foreach (string name in added)
            {
                yield return $"{after.ShortName}: version {version} adds the option {after.Option(name)}, generated here under its default"
                    + $" ({@is[name].Default ?? "none"}): decide by hand whether this project takes another value.";
            }

            foreach (string name in was.Keys.Intersect(@is.Keys).Order(StringComparer.Ordinal))
            {
                string[] gone = [.. was[name].Choices.Except(@is[name].Choices)];
                string[] come = [.. @is[name].Choices.Except(was[name].Choices)];

                if (gone.Length > 0)
                {
                    yield return $"{after.ShortName}: version {version} no longer offers {string.Join(", ", gone)} for {after.Option(name)}: decide by hand.";
                }

                if (come.Length > 0)
                {
                    yield return $"{after.ShortName}: version {version} offers {string.Join(", ", come)} for {after.Option(name)} as well.";
                }

                if (was[name].Default != @is[name].Default)
                {
                    yield return $"{after.ShortName}: version {version} changes the default of {after.Option(name)} from {was[name].Default ?? "none"} to {@is[name].Default ?? "none"}.";
                }
            }
        }
    }

    private static void RequireTheProjectsPaths(string olderTree, string root, IReadOnlyCollection<string> projectFiles, string name)
    {
        string[] generated = [.. TreeFiles(olderTree).Where(path => path.EndsWith(".csproj", StringComparison.Ordinal) || path == name + ".sln")];
        var held = new HashSet<string>(projectFiles, StringComparer.Ordinal);

        if (!held.Contains(name + ".sln") || !generated.Any(held.Contains))
        {
            string example = generated.FirstOrDefault(path => path.EndsWith(".csproj", StringComparison.Ordinal)) ?? name + ".sln";
            throw new InvalidOperationException(
                $"Generating '{name}', the name cleanarch.json records, writes paths this project does not hold, such as {example}, in {root}: "
                + "the project's name or its folders' initials are not the generation's, so every file would read as changed. Compare by hand.");
        }
    }

    private static string[] ProjectFiles(string root)
    {
        (int exit, string output, _) = Capture(root, "git", "ls-files", "--cached", "--others", "--exclude-standard", "-z");

        return exit == 0
            ? [.. output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Where(path => File.Exists(Path.Combine(root, path))).Order(StringComparer.Ordinal)]
            : [.. TreeFiles(root)];
    }

    private static IEnumerable<string> TreeFiles(string root)
    {
        var pending = new Stack<string>([root]);

        while (pending.Count > 0)
        {
            string directory = pending.Pop();

            foreach (string folder in Directory.EnumerateDirectories(directory).Where(folder => !_skippedFolders.Contains(Path.GetFileName(folder))))
            {
                pending.Push(folder);
            }

            foreach (string file in Directory.EnumerateFiles(directory))
            {
                yield return Path.GetRelativePath(root, file).Replace('\\', '/');
            }
        }
    }

    private static Dictionary<string, string> JoinedPaths(string olderTree, string newerTree, Provenance project)
    {
        var joined = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Replay replay in PlanOf(project).Replays)
        {
            foreach ((string tree, string folder) in new[] { olderTree, newerTree }.SelectMany(tree => Resolve(tree, replay.Folder).Select(folder => (tree, folder))))
            {
                string readme = Path.Combine(tree, folder, "README.md");
                string[] lines = File.Exists(readme) ? File.ReadAllLines(readme) : [];
                int start = Array.IndexOf(lines, _joiningSection);

                for (int index = start + 1; start >= 0 && index < lines.Length && !lines[index].StartsWith("## ", StringComparison.Ordinal); index++)
                {
                    if (_joinedPath.Match(lines[index]) is { Success: true } edit)
                    {
                        joined.TryAdd(edit.Groups["path"].Value, folder);
                    }
                }
            }
        }

        return joined;
    }

    private static IEnumerable<string> Resolve(string tree, string folder)
    {
        string parent = Path.Combine(tree, Path.GetDirectoryName(folder)!);

        return Directory.Exists(parent)
            ? Directory.EnumerateDirectories(parent, Path.GetFileName(folder)).Select(found => Path.GetRelativePath(tree, found).Replace('\\', '/'))
            : [];
    }

    private static Comparison Compare(
        string older,
        string newer,
        string project,
        IReadOnlyCollection<string> projectFiles,
        string name,
        Dictionary<string, string> joined,
        IReadOnlySet<string>? heldGuids)
    {
        var olderFiles = new HashSet<string>(TreeFiles(older), StringComparer.Ordinal);
        var newerFiles = new HashSet<string>(TreeFiles(newer), StringComparer.Ordinal);
        var held = new HashSet<string>(projectFiles, StringComparer.Ordinal);
        Func<string, bool> binary = BinaryPatterns(project);

        var findings = new List<Finding>();
        var bootstrap = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        var scaffolds = new List<string>();
        var own = new List<string>();
        int unchanged = 0;
        string? provenance = null;

        Dictionary<string, string> scaffolded = olderFiles.Union(newerFiles)
            .Where(path => Path.GetFileName(path) == _scaffoldDescriptor && Regex.IsMatch(path, @"^Src/(?:Presentation|Web/apps)/[^/]+/scaffold\.json$", RegexOptions.None, _timeout))
            .Select(path => path[..^(_scaffoldDescriptor.Length + 1)])
            .ToDictionary(folder => folder, folder => folder.StartsWith("Src/Web/", StringComparison.Ordinal) ? "web-scaffold" : "presentation-scaffold", StringComparer.Ordinal);
        bool addsToSolution = scaffolded.Keys.Any(folder => new[] { older, newer }
            .Select(tree => Path.Combine(tree, folder, _scaffoldDescriptor))
            .Where(File.Exists)
            .Any(descriptor => JsonNode.Parse(File.ReadAllText(descriptor))?["kind"]?.GetValue<string>() is "dotnet" or "written"));
        string? Scaffold(string path) => scaffolded.Keys.FirstOrDefault(folder => path.StartsWith(folder + "/", StringComparison.Ordinal));
        string Read(string root, string path) => Path.Combine(root, path);
        bool Same(string left, string right) => SameContent(left, right, binary, heldGuids);
        void Listed(string writer, string path)
        {
            if (!bootstrap.TryGetValue(writer, out List<string>? paths))
            {
                bootstrap[writer] = paths = [];
            }

            paths.Add(path);
        }

        foreach (string path in olderFiles.Union(newerFiles).Union(held).Order(StringComparer.Ordinal))
        {
            bool inOlder = olderFiles.Contains(path);
            bool inNewer = newerFiles.Contains(path);
            bool inProject = held.Contains(path);
            string? folder = Scaffold(path);

            if (_writtenByBootstrap.FirstOrDefault(rule => rule.Path.IsMatch(path)) is { Writer: { } writer })
            {
                if (inProject)
                {
                    Listed(writer, path);
                }

                continue;
            }

            if (path == _projectFileName)
            {
                provenance = "cleanarch.json is the project's record: set template.version to the target version, and keep the rest.";
                continue;
            }

            if (folder is not null)
            {
                string inside = path[(folder.Length + 1)..];
                string task = $"dotnet run Tools/Tasks.cs {scaffolded[folder]} {(scaffolded[folder] == "web-scaffold" ? Path.GetFileName(folder) : name + "." + Path.GetFileName(folder)[(Path.GetFileName(folder).IndexOf('.', StringComparison.Ordinal) + 1)..])}";

                if (inside == _scaffoldDescriptor)
                {
                    if (!(inOlder && inNewer && Same(Read(older, path), Read(newer, path))))
                    {
                        scaffolds.Add($"{path}: {(inOlder ? inNewer ? "changed" : "removed" : "added")} by the target version; bootstrap's {scaffolded[folder]} "
                            + "created the part from it, so take what the new descriptor brings by hand.");
                    }

                    continue;
                }

                if (inside.StartsWith("overlay/", StringComparison.Ordinal))
                {
                    if (!(inOlder && inNewer && Same(Read(older, path), Read(newer, path))))
                    {
                        string target = folder + "/" + inside["overlay/".Length..];
                        findings.Add(new Finding(Kind.Merge, target, $"{scaffolded[folder]} laid {path} over the part it created", inOlder ? path : null, inNewer ? path : null));
                    }

                    continue;
                }

                if (inProject && !inOlder && !inNewer)
                {
                    Listed(task, path);
                    continue;
                }
            }

            string? edited = _editedByBootstrap.FirstOrDefault(rule => rule.Path.IsMatch(path)).Editor
                ?? (path == name + ".sln" && addsToSolution ? "presentation-scaffold and web-scaffold add the .NET projects they create to it" : null);
            string? join = joined.TryGetValue(path, out string? part) ? $"the join of {part} edits it" : null;
            string? Note(params string?[] notes) => notes.OfType<string>().ToArray() is { Length: > 0 } some ? string.Join("; ", some) : null;

            if (inOlder && inNewer)
            {
                if (Same(Read(older, path), Read(newer, path)))
                {
                    unchanged++;
                }
                else if (!inProject)
                {
                    findings.Add(new Finding(Kind.Merge, path, Note("the project deleted it", join), path, path));
                }
                else if (edited is null && join is null && Same(Read(older, path), Read(project, path)))
                {
                    findings.Add(new Finding(Kind.Take, path, null, path, path));
                }
                else
                {
                    findings.Add(new Finding(Kind.Merge, path, Note(edited, join), path, path));
                }
            }
            else if (inNewer)
            {
                if (!inProject)
                {
                    findings.Add(new Finding(Kind.Added, path, null, null, path));
                }
                else if (Same(Read(newer, path), Read(project, path)))
                {
                    unchanged++;
                }
                else
                {
                    findings.Add(new Finding(Kind.Added, path, "the project holds a file of its own at this path: merge by hand", null, path));
                }
            }
            else if (inOlder)
            {
                if (!inProject)
                {
                    unchanged++;
                }
                else
                {
                    findings.Add(new Finding(Kind.Removed, path, Same(Read(older, path), Read(project, path)) ? null : "the project changed it: decide by hand", path, null));
                }
            }
            else
            {
                own.Add(path);
            }
        }

        return new Comparison(findings, bootstrap, scaffolds, own, unchanged, provenance);
    }

    private static Func<string, bool> BinaryPatterns(string root)
    {
        string attributes = Path.Combine(root, ".gitattributes");
        Regex[] patterns = File.Exists(attributes)
            ? [.. File.ReadAllLines(attributes)
                .Select(line => line.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
                .Where(fields => fields.Length > 1 && !fields[0].StartsWith('#') && !fields[0].Contains('/') && fields.Skip(1).Any(field => field is "binary" or "-text"))
                .Select(fields => new Regex("^" + Regex.Escape(fields[0]).Replace(@"\*", ".*", StringComparison.Ordinal).Replace(@"\?", ".", StringComparison.Ordinal) + "$", RegexOptions.None, _timeout))]
            : [];

        return path => patterns.Any(pattern => pattern.IsMatch(Path.GetFileName(path)));
    }

    private static bool SameContent(string left, string right, Func<string, bool> binary, IReadOnlySet<string>? heldGuids)
    {
        byte[] leftBytes = File.ReadAllBytes(left);
        byte[] rightBytes = File.ReadAllBytes(right);

        if (leftBytes.AsSpan().SequenceEqual(rightBytes))
        {
            return true;
        }

        if (binary(left) || IsBinary(leftBytes) || IsBinary(rightBytes))
        {
            return false;
        }

        string leftText = Normalised(leftBytes);
        string rightText = Normalised(rightBytes);

        if (leftText == rightText)
        {
            return true;
        }

        if (left.EndsWith(".sln", StringComparison.Ordinal))
        {
            return CanonicalSolution(leftText) == CanonicalSolution(rightText);
        }

        if (heldGuids is not null)
        {
            (leftText, rightText) = (WithoutDrawnGuids(leftText, heldGuids), WithoutDrawnGuids(rightText, heldGuids));
        }

        return leftText == rightText
            || (left.EndsWith(".cs", StringComparison.Ordinal) && CanonicalCSharp(leftText) == CanonicalCSharp(rightText));
    }

    private static string WithoutDrawnGuids(string text, IReadOnlySet<string> heldGuids)
    {
        var drawn = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        return _guid.Replace(text, match => heldGuids.Contains(match.Value)
            ? match.Value
            : $"<GUID drawn #{(drawn.TryGetValue(match.Value, out int order) ? order : drawn[match.Value] = drawn.Count + 1)}>");
    }

    private static string CanonicalSolution(string text)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (Match entry in _solutionEntry.Matches(text))
        {
            string path = entry.Groups["path"].Value;
            seen[path] = seen.GetValueOrDefault(path) + 1;
            text = Regex.Replace(text, Regex.Escape(entry.Groups["id"].Value), $"<{path}#{seen[path]}>", RegexOptions.IgnoreCase, _timeout);
        }

        return text;
    }

    private static bool IsBinary(byte[] bytes) => bytes.AsSpan(0, Math.Min(bytes.Length, 8000)).Contains((byte)0);

    private static string Normalised(byte[] bytes)
    {
        string text = Encoding.UTF8.GetString(bytes);

        return (text.StartsWith('﻿') ? text[1..] : text).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string CanonicalCSharp(string text)
    {
        var usings = new List<string>();
        var rest = new List<string>();
        bool header = true;

        foreach (string line in text.Split('\n'))
        {
            if (header && _usingDirective.IsMatch(line))
            {
                usings.Add(line);
            }
            else if (!(header && line.Length == 0))
            {
                header &= line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith('#');
                rest.Add(line);
            }
        }

        return string.Join('\n', usings.Order(StringComparer.Ordinal)) + "\n\n" + string.Join('\n', rest);
    }

    private static string TemplateDiff(string scratch, string older, string newer, Finding finding)
    {
        if (finding.Path.EndsWith(".sln", StringComparison.Ordinal) && finding.Older is not null && finding.Newer is not null)
        {
            string canonical = Path.Combine(scratch, "canonical");

            foreach ((string tree, string path) in new[] { (older, finding.Older), (newer, finding.Newer) })
            {
                string copy = Path.Combine(canonical, Path.GetFileName(tree), path);
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                File.WriteAllText(copy, CanonicalSolution(Normalised(File.ReadAllBytes(Path.Combine(tree, path)))));
            }

            (older, newer) = (Path.Combine(canonical, Path.GetFileName(older)), Path.Combine(canonical, Path.GetFileName(newer)));
        }

        string Side(string tree, string? path) => path is null ? "/dev/null" : Path.GetRelativePath(scratch, Path.Combine(tree, path)).Replace('\\', '/');

        (int exit, string output, string errors) = Capture(scratch, "git", "diff", "--no-index", "--no-color", "--no-prefix", "--", Side(older, finding.Older), Side(newer, finding.Newer));

        return exit is 0 or 1
            ? $"{finding.Path}:\n{output.TrimEnd()}"
            : $"{finding.Path}: no diff, since git diff --no-index failed: {errors.Trim()}";
    }

    private static string ChangelogBetween(string changelog, string from, string to)
    {
        var kept = new StringBuilder();
        bool keeping = false;

        foreach (string line in changelog.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                Match heading = _changelogHeading.Match(line);
                keeping = heading.Success
                    && CompareVersions(heading.Groups["version"].Value, from) > 0
                    && CompareVersions(heading.Groups["version"].Value, to) <= 0;
            }

            if (keeping)
            {
                kept.Append(line).Append('\n');
            }
        }

        return kept.Length > 0 ? kept.ToString().TrimEnd() : $"CHANGELOG.md holds no entry after {from} up to {to}.";
    }

    private static void Print(Report report, TextWriter output)
    {
        Comparison comparison = report.Comparison;
        void Section(string title, IReadOnlyCollection<string> lines)
        {
            output.WriteLine();
            output.WriteLine($"{title} ({lines.Count})");

            foreach (string line in lines)
            {
                output.WriteLine("  " + line);
            }
        }

        string[] Of(Kind kind) => [.. comparison.Findings.Where(finding => finding.Kind == kind).Select(finding => finding.Note is null ? finding.Path : $"{finding.Path}   ({finding.Note})")];

        output.WriteLine();
        output.WriteLine($"Upgrade report for {report.Name}: from {report.From} to {report.To}. Nothing was written to this project.");
        Section("Decisions to take by hand", [.. report.Decisions, .. comparison.Provenance is null ? Array.Empty<string>() : [comparison.Provenance]]);
        Section($"Take as {report.To} writes it: {report.To} changed these and the project did not", Of(Kind.Take));
        Section($"Merge by hand: {report.To} changed these and so did the project; the diffs from {report.From} close this report", Of(Kind.Merge));
        Section($"Added by {report.To}", Of(Kind.Added));
        Section($"Removed by {report.To}", Of(Kind.Removed));
        Section("Changed scaffold descriptors", comparison.Scaffolds);
        Section(
            "From bootstrap or a framework's CLI, not compared",
            [.. comparison.Bootstrap.Select(group => $"{group.Key}: {group.Value.Count} file(s), such as {group.Value[0]}")]);
        output.WriteLine();
        output.WriteLine($"The project's own files, in neither generation and not concerned: {comparison.Own.Count}{(report.Json is null ? string.Empty : $", listed in {report.Json}")}.");
        output.WriteLine($"Files {report.To} left unchanged, or that need nothing: {comparison.Unchanged}.");
        output.WriteLine(report.Kept is null
            ? "--keep <folder> keeps the two generations outside the project, so a file to take can be copied from there."
            : $"The two generations are kept in {report.Kept}: take a file from {Path.Combine(report.Kept, report.To)}, "
                + $"and merge one three ways with git merge-file <path> {Path.Combine(report.Kept, report.From)}/<path> {Path.Combine(report.Kept, report.To)}/<path>.");
        output.WriteLine("After taking files, run `dotnet run Tools/Tasks.cs format-fix`: it orders the using directives as bootstrap did.");
        output.WriteLine();
        output.WriteLine($"Changelog after {report.From} up to {report.To}");
        output.WriteLine(report.Changelog);

        if (report.Diffs.Count > 0)
        {
            output.WriteLine();
            output.WriteLine($"The diffs from {report.From} to {report.To} of the files to merge by hand");

            foreach (string diff in report.Diffs)
            {
                output.WriteLine();
                output.WriteLine(diff);
            }
        }
    }

    private static JsonObject ToJson(Report report)
    {
        JsonArray Paths(Kind kind) => [.. report.Comparison.Findings.Where(finding => finding.Kind == kind)
            .Select(finding => (JsonNode)new JsonObject { ["path"] = finding.Path, ["note"] = finding.Note })];

        return new JsonObject
        {
            ["name"] = report.Name,
            ["from"] = report.From,
            ["to"] = report.To,
            ["kept"] = report.Kept,
            ["decisions"] = new JsonArray([.. report.Decisions.Select(decision => (JsonNode)JsonValue.Create(decision))]),
            ["provenance"] = report.Comparison.Provenance,
            ["take"] = Paths(Kind.Take),
            ["merge"] = Paths(Kind.Merge),
            ["added"] = Paths(Kind.Added),
            ["removed"] = Paths(Kind.Removed),
            ["scaffolds"] = new JsonArray([.. report.Comparison.Scaffolds.Select(line => (JsonNode)JsonValue.Create(line))]),
            ["bootstrap"] = new JsonObject(report.Comparison.Bootstrap.Select(group =>
                KeyValuePair.Create(group.Key, (JsonNode?)new JsonArray([.. group.Value.Select(path => (JsonNode)JsonValue.Create(path))])))),
            ["own"] = new JsonArray([.. report.Comparison.Own.Select(path => (JsonNode)JsonValue.Create(path))]),
            ["unchanged"] = report.Comparison.Unchanged,
            ["diffs"] = new JsonArray([.. report.Diffs.Select(diff => (JsonNode)JsonValue.Create(diff))]),
            ["changelog"] = report.Changelog,
        };
    }

    private static void Step(string directory, string executable, string[] arguments)
    {
        Console.WriteLine($"> {executable} {string.Join(' ', arguments)}   (in {directory})");
        Console.Out.Flush();

        using Process process = Start(directory, executable, arguments, redirect: false);
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"'{executable} {string.Join(' ', arguments)}' exited with {process.ExitCode}.");
        }
    }

    private static (int ExitCode, string Output, string Errors) Capture(string directory, string executable, params string[] arguments)
    {
        using Process process = Start(directory, executable, arguments, redirect: true);
        Task<string> errors = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, output, errors.GetAwaiter().GetResult());
    }

    private static Process Start(string directory, string executable, string[] arguments, bool redirect)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = executable,
            UseShellExecute = false,
            WorkingDirectory = directory,
            RedirectStandardInput = true,
            RedirectStandardOutput = redirect,
            RedirectStandardError = redirect,
        };
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            Process process = Process.Start(startInfo) ?? throw new InvalidOperationException($"'{executable}' started no process.");
            process.StandardInput.Close();
            return process;
        }
        catch (Win32Exception)
        {
            throw new InvalidOperationException($"'{executable}' is not on PATH.");
        }
    }

    private static Regex Rx(string pattern) => new(pattern, RegexOptions.None, _timeout);

    private static int SelfTest()
    {
        string scratch = Directory.CreateTempSubdirectory("upgrade-report-self-test-").FullName;
        var problems = new List<string>();
        int checks = 0;
        void Expect(bool condition, string problem)
        {
            checks++;

            if (!condition)
            {
                problems.Add(problem);
            }
        }

        try
        {
            ClassificationCases(scratch, Expect);
            OptionCases(Expect);
            VersionCases(Expect);
            PlanCases(Expect);
            RefusalCases(scratch, Expect);
            PackageCases(scratch, Expect);
            ChangelogCases(Expect);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }

        Console.WriteLine($"Self-test: {checks} check(s), {problems.Count} problem(s).");
        problems.ForEach(problem => Console.Error.WriteLine("  " + problem));
        return problems.Count == 0 && checks > 0 ? 0 : 1;
    }

    private static void ClassificationCases(string scratch, Action<bool, string> expect)
    {
        string older = Path.Combine(scratch, "older");
        string newer = Path.Combine(scratch, "newer");
        string project = Path.Combine(scratch, "project");
        void Put(string root, string path, string text)
        {
            string full = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
        }

        void Three(string path, string? inOlder, string? inNewer, string? inProject)
        {
            foreach ((string root, string? text) in new[] { (older, inOlder), (newer, inNewer), (project, inProject) })
            {
                if (text is not null)
                {
                    Put(root, path, text);
                }
            }
        }

        Three("same.md", "a\n", "a\n", "a\n");
        Three("take.md", "a\n", "b\n", "a\n");
        Three("merge.md", "a\n", "b\n", "c\n");
        Three("deleted.md", "a\n", "b\n", null);
        Three("added.md", null, "b\n", null);
        Three("clash.md", null, "b\n", "c\n");
        Three("removed.md", "a\n", null, "a\n");
        Three("removed-changed.md", "a\n", null, "c\n");
        Three("own.md", null, null, "c\n");
        Three("line-endings.md", "a\nb\n", "a\nc\n", "﻿a\r\nb\r\n");
        Three("real-change.md", "a\nb\n", "a\nc\n", "﻿a\r\nB\r\n");
        Three("Usings.cs", "using B;\nusing A;\n\nnamespace N;\n", "using B;\nusing A;\n\nnamespace M;\n", "using A;\nusing B;\nnamespace N;\n");
        Three("UsingRemoved.cs", "using B;\nusing A;\n\nnamespace N;\n", "using B;\nusing A;\n\nnamespace M;\n", "using A;\n\nnamespace N;\n");
        Three("Sample.Shop.sln", "a\n", "b\n", "a\n");
        Three(
            "Other.sln",
            Solution("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222", "Src\\A.csproj"),
            Solution("33333333-3333-3333-3333-333333333333", "44444444-4444-4444-4444-444444444444", "Src\\A.csproj"),
            Solution("55555555-5555-5555-5555-555555555555", "66666666-6666-6666-6666-666666666666", "Src\\A.csproj"));
        Three(
            "Moved.sln",
            Solution("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222", "Src\\A.csproj"),
            Solution("33333333-3333-3333-3333-333333333333", "44444444-4444-4444-4444-444444444444", "Src\\B.csproj"),
            Solution("55555555-5555-5555-5555-555555555555", "66666666-6666-6666-6666-666666666666", "Src\\A.csproj"));
        const string heldGuid = "77777777-7777-7777-7777-777777777777";
        const string otherHeldGuid = "88888888-8888-8888-8888-888888888888";
        string[] drawnGuids = ["aaaaaaaa-0000-4000-8000-000000000001", "bbbbbbbb-0000-4000-8000-000000000002", "cccccccc-0000-4000-8000-000000000003"];
        string Secrets(string guid, string rest = "") => $"<UserSecretsId>{guid}</UserSecretsId>\n{rest}";
        Three("Drawn.csproj", Secrets(drawnGuids[0]), Secrets(drawnGuids[1]), Secrets(drawnGuids[2]));
        Three("DrawnTake.csproj", Secrets(drawnGuids[0], "<A>1</A>\n"), Secrets(drawnGuids[1], "<A>2</A>\n"), Secrets(drawnGuids[2].ToUpperInvariant(), "<A>1</A>\n"));
        Three("HeldChanged.csproj", Secrets(heldGuid), Secrets(otherHeldGuid), Secrets(heldGuid));
        Three("HeldEdited.csproj", Secrets(heldGuid, "<A>1</A>\n"), Secrets(heldGuid, "<A>2</A>\n"), Secrets(drawnGuids[2], "<A>1</A>\n"));
        Three("DrawnTwo.csproj", Secrets(drawnGuids[0], drawnGuids[1] + "\n<A>1</A>\n"), Secrets(drawnGuids[1], drawnGuids[0] + "\n<A>2</A>\n"), Secrets(drawnGuids[2], drawnGuids[2] + "\n<A>1</A>\n"));
        Three(".config/dotnet-tools.json", "{}\n", "{ }\n", "{}\n");
        Three("docker-compose.yml", "a\n", "b\n", "a\n");
        Three(_projectFileName, "{}\n", "{ }\n", "{}\n");
        Three("Src/Infrastructure/SS.Infrastructure.Persistence/Migrations/1_InitialCreate.cs", null, null, "class M;\n");
        Three("Src/Clients/SS.Client/Generated/ApiClient.cs", "a\n", "b\n", "c\n");
        Three("Tests/Integration/SS.Api.IntegrationTests/OpenApi/v1.json", null, null, "{}\n");
        Three("Src/Web/apps/Site/scaffold.json", "{\"version\":\"1\"}\n", "{\"version\":\"2\"}\n", null);
        Three("Src/Presentation/SS.Portal/scaffold.json", "{\"kind\":\"dotnet\"}\n", "{\"kind\":\"dotnet\"}\n", null);
        Three("Src/Web/apps/Site/overlay/src/main.ts", "a\n", "b\n", null);
        Three("Src/Web/apps/Site/src/main.ts", null, null, "a\n");
        Three("Src/Web/apps/Site/vite.config.ts", null, null, "x\n");
        Three("Src/Web/apps/Site/Dockerfile", "a\n", "b\n", "a\n");
        Three("Src/Web/apps/Plain/package.json", "{}\n", "{ }\n", "{}\n");
        Three(".gitattributes", "* text=auto\n*.png binary\n", "* text=auto\n*.png binary\n", "* text=auto\n*.png binary\n");
        Three("logo.png", "a\r\nb", "a\r\nc", "a\nb");

        string[] projectFiles = [.. TreeFiles(project)];
        var joined = new Dictionary<string, string>(StringComparer.Ordinal) { ["docker-compose.yml"] = "Src/Web/apps/Other" };
        Comparison comparison = Compare(
            older, newer, project, projectFiles, "Sample.Shop", joined, new HashSet<string>([heldGuid, otherHeldGuid], StringComparer.OrdinalIgnoreCase));
        Kind? KindOf(string path) => comparison.Findings.FirstOrDefault(finding => finding.Path == path)?.Kind;

        expect(KindOf("same.md") is null, "A file no version changed was reported.");
        expect(KindOf("take.md") == Kind.Take, "A file the template changed and the project did not is not to take as the target writes it.");
        expect(KindOf("merge.md") == Kind.Merge, "A file the template and the project both changed is not to merge by hand.");
        expect(KindOf("deleted.md") == Kind.Merge, "A file the template changed and the project deleted is not a decision to merge.");
        expect(KindOf("added.md") == Kind.Added, "A file the target adds is not reported as added.");
        expect(comparison.Findings.Any(finding => finding.Path == "clash.md" && finding.Kind == Kind.Added && finding.Note is not null), "A file the target adds where the project holds its own is not flagged.");
        expect(KindOf("removed.md") == Kind.Removed, "A file the target no longer writes is not reported as removed.");
        expect(comparison.Findings.Any(finding => finding.Path == "removed-changed.md" && finding.Note is not null), "A removed file the project changed is not flagged.");
        expect(comparison.Own.SequenceEqual(["own.md"]), $"The project's own files are [{string.Join(", ", comparison.Own)}], not [own.md].");
        expect(KindOf("line-endings.md") == Kind.Take, "A CRLF and a BOM .gitattributes and the editor allow read as a change of the project.");
        expect(KindOf("real-change.md") == Kind.Merge, "A real change was hidden by the line-ending normalisation.");
        expect(KindOf("Usings.cs") == Kind.Take, "Using directives bootstrap ordered read as a change of the project.");
        expect(KindOf("UsingRemoved.cs") == Kind.Merge, "A using directive the project removed was hidden by the ordering.");
        expect(KindOf("Other.sln") is null, "A solution whose GUIDs dotnet new drew anew read as changed.");
        expect(KindOf("Moved.sln") == Kind.Take, "A solution entry the template moved was hidden by the GUID reading.");
        expect(KindOf("Drawn.csproj") is null, "A GUID dotnet new drew for a guid the template declares read as a change.");
        expect(KindOf("DrawnTake.csproj") == Kind.Take, "A change beside a drawn GUID was hidden, or the drawn GUID read as the project's own change.");
        expect(KindOf("HeldChanged.csproj") == Kind.Take, "A GUID the package holds as written, which the target changed, was read as drawn.");
        expect(KindOf("HeldEdited.csproj") == Kind.Merge, "A GUID the package holds, which the project replaced, was read as drawn.");
        expect(KindOf("DrawnTwo.csproj") == Kind.Merge, "Two drawn GUIDs read as one, which hides that the project made them the same.");
        expect(KindOf("Sample.Shop.sln") == Kind.Merge, "The solution, which the scaffolds edit, was offered to take as is.");
        expect(KindOf(".config/dotnet-tools.json") == Kind.Merge, "The tool manifest, which bootstrap edits, was offered to take as is.");
        expect(KindOf("Src/Web/apps/Plain/package.json") == Kind.Merge, "A web app's package.json, whose versions web-scaffold pins, was offered to take as is.");
        expect(KindOf("docker-compose.yml") == Kind.Merge, "A file a part's join edits was offered to take as is.");
        expect(KindOf(_projectFileName) is null && comparison.Provenance is not null, "cleanarch.json was compared as a template file instead of named as the project's record.");
        expect(comparison.Bootstrap.Values.SelectMany(paths => paths).Contains("Src/Infrastructure/SS.Infrastructure.Persistence/Migrations/1_InitialCreate.cs"), "A migration is not listed as written by bootstrap.");
        expect(comparison.Bootstrap.Values.SelectMany(paths => paths).Contains("Tests/Integration/SS.Api.IntegrationTests/OpenApi/v1.json"), "The OpenAPI reference is not listed as written by bootstrap.");
        expect(KindOf("Src/Clients/SS.Client/Generated/ApiClient.cs") is null, "The generated client was compared, though api-client rewrites it.");
        expect(comparison.Bootstrap.Any(group => group.Key.Contains("web-scaffold Site", StringComparison.Ordinal) && group.Value.Contains("Src/Web/apps/Site/vite.config.ts")), "A file a framework's CLI created is not listed under web-scaffold.");
        expect(comparison.Scaffolds.Any(line => line.StartsWith("Src/Web/apps/Site/scaffold.json", StringComparison.Ordinal)), "A changed scaffold descriptor is not reported.");
        expect(comparison.Findings.Any(finding => finding.Path == "Src/Web/apps/Site/src/main.ts" && finding.Kind == Kind.Merge), "A changed overlay file is not mapped to the file the scaffold laid it on.");
        expect(KindOf("Src/Web/apps/Site/Dockerfile") == Kind.Take, "A template file beside a scaffold descriptor is not compared.");
        expect(KindOf("logo.png") == Kind.Merge, "A binary file's CRLF was normalised.");
        expect(
            CanonicalCSharp("using A;\nnamespace N;\nstring s = \"\"\"\nusing B;\nusing C;\n\"\"\";\n") != CanonicalCSharp("using A;\nnamespace N;\nstring s = \"\"\"\nusing C;\nusing B;\n\"\"\";\n"),
            "Lines of a string literal after the header were ordered as using directives.");
    }

    private static string Solution(string folder, string project, string path) =>
        $"Project(\"{{2150E333-8FDC-42A3-9474-1A3956D46DE8}}\") = \"Src\", \"Src\", \"{{{folder}}}\"\nEndProject\n"
        + $"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"A\", \"{path}\", \"{{{project}}}\"\nEndProject\n"
        + $"\t\t{{{project}}} = {{{folder}}}\n";

    private static void OptionCases(Action<bool, string> expect)
    {
        static JsonObject Choice(string defaultValue, params string[] choices) => new()
        {
            ["type"] = "parameter",
            ["datatype"] = "choice",
            ["defaultValue"] = defaultValue,
            ["choices"] = new JsonArray([.. choices.Select(choice => (JsonNode)new JsonObject { ["choice"] = choice })]),
        };

        static JsonObject Text(string defaultValue) => new() { ["type"] = "parameter", ["datatype"] = "text", ["defaultValue"] = defaultValue };

        static PackageContent Content(JsonObject symbols, JsonArray? postActions = null) => new(
            "x",
            new Dictionary<string, Template>(StringComparer.Ordinal)
            {
                [_mainTemplate] = new Template(_mainTemplate, "cleanarch-webapi", symbols, postActions ?? [], new Dictionary<string, string>(StringComparer.Ordinal) { ["webName"] = "web-name", ["siteName"] = "site-name" }),
            },
            null,
            null,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            false);

        PackageContent older = Content(new JsonObject
        {
            ["auth"] = Choice("local", "local", "external", "none"),
            ["webName"] = Text("Site"),
            ["guard"] = Choice("none", "refused"),
        });
        PackageContent newer = Content(new JsonObject
        {
            ["auth"] = Choice("external", "local", "external"),
            ["siteName"] = Text("Site"),
            ["guard"] = Choice("none", "refused"),
        });
        Provenance project = Fixture(options: [("auth", JsonValue.Create("none")!), ("webName", JsonValue.Create("Shop")!)]);
        string[] changes = [.. OptionChanges(older, newer, project, "2.1.0")];

        expect(changes.Any(change => change.Contains("removes the option --web-name", StringComparison.Ordinal) && change.Contains("never assumed", StringComparison.Ordinal)),
            "A renamed option is not reported as removed with no rename assumed: " + string.Join(" | ", changes));
        expect(changes.Any(change => change.Contains("adds the option --site-name", StringComparison.Ordinal)), "An added option is not reported.");
        expect(changes.Any(change => change.Contains("no longer offers none for --auth", StringComparison.Ordinal)), "A choice the target drops is not reported.");
        expect(changes.Any(change => change.Contains("default of --auth", StringComparison.Ordinal)), "A changed default is not reported.");
        expect(!changes.Any(change => change.Contains("guard", StringComparison.Ordinal)), "A refusal guard was reported as an option.");

        var notes = new List<string>();
        string[] arguments = Arguments(newer.Templates[_mainTemplate], [("siteName", "Shop"), ("webName", "Shop")], "2.1.0", notes);
        expect(arguments.SequenceEqual(["--site-name", "Shop"]) && notes.Count == 1, "An option the version lacks was passed, or not noted, instead of left out.");
        expect(Throws(() => Arguments(newer.Templates[_mainTemplate], [("auth", "none")], "2.1.0", notes)), "A recorded value the version no longer offers was passed to dotnet new.");

        JsonArray restore = [new JsonObject { ["actionId"] = "210D431B-A78B-4D2F-B762-4ED3E3EA9025" }];
        JsonArray manual = [new JsonObject { ["actionId"] = _manualInstructions }];
        expect(Throws(() => RefuseRunningPostActions(Content(new JsonObject(), restore), "2.1.0")), "A package declaring a restore post-action was accepted.");
        expect(!Throws(() => RefuseRunningPostActions(Content(new JsonObject(), manual), "2.1.0")), "A package declaring only manual instructions was refused.");
    }

    private static void VersionCases(Action<bool, string> expect)
    {
        Release[] catalog = [new("2.0.0", null), new("2.1.0", null), new("2.2.0-rc.1", null), new("1.0.0", null)];

        expect(ChooseVersions("2.0.0", null, catalog, "P").To.Version == "2.1.0", "The default target is not the latest stable version.");
        expect(ChooseVersions("2.0.0", "2.2.0-rc.1", catalog, "P").To.Version == "2.2.0-rc.1", "A prerelease named by --to was refused.");
        expect(Throws(() => ChooseVersions("2.1.0", null, catalog, "P")), "A project at the latest stable version was given a prerelease or itself.");
        expect(Throws(() => ChooseVersions("2.1.0", "2.0.0", catalog, "P")), "An older target was accepted.");
        expect(Throws(() => ChooseVersions("2.1.0", "2.1.0", catalog, "P")), "The project's own version was accepted as the target.");
        expect(Throws(() => ChooseVersions("2.0.0", "3.0.0", catalog, "P")), "A target no source holds was accepted.");
        expect(Throws(() => ChooseVersions("1.5.0", null, catalog, "P")), "A project version no source holds was accepted.");
        expect(CompareVersions("2.0.0-rc.2", "2.0.0-rc.10") < 0 && CompareVersions("2.0.0", "2.0.0-rc.10") > 0, "Prereleases are not ordered as Semantic Versioning orders them.");
    }

    private static void PlanCases(Action<bool, string> expect)
    {
        Provenance project = Fixture(
            options: [("auth", JsonValue.Create("external")!), ("files", JsonValue.Create(false)!)],
            web: new WebRecord("sample-shop", [new WebApp("Site", "vite-react", "public", "spa"), new WebApp("BackOffice", "angular", "admin", "ssr")], ["tokens"]),
            presentation: [("Api", "api"), ("Worker", "worker"), ("Admin", "blazor-server")]);
        Plan plan = PlanOf(project);
        string Values(IEnumerable<(string Symbol, string Value)> values) => string.Join(" ", values.Select(value => value.Symbol + "=" + value.Value));

        expect(Values(plan.Generation) == "auth=external files=false web=vite-react webName=Site webAudience=public webRendering=spa presentation=api presentation=worker",
            "The generation does not take the recorded options, the first app and the hosts under their generated names: " + Values(plan.Generation));
        expect(string.Join(" ", plan.Replays.Select(replay => replay.Name)) == "BackOffice tokens Admin",
            "The item templates do not replay the apps, packages and hosts generation cannot express, in the recorded order.");
        expect(plan.Replays[0].Values.Contains(("rendering", "ssr")) && plan.Replays[0].ProjectOptions.Contains(("auth", "external")),
            "The second app is not replayed with its recorded rendering and the project's options.");
        expect(!plan.Replays[0].Values.Any(value => value.Symbol == "port"),
            "A web app whose record holds no port was replayed with one: it keeps the framework's default.");

        Provenance ported = Fixture(web: new WebRecord("sample-shop", [new WebApp("Site", "vite-react", "public", "spa"), new WebApp("Shop", "angular", "public", "spa", 8083)], []));
        expect(PlanOf(ported).Replays.Single().Values.Contains(("port", "8083")),
            "A web app is not replayed on the port its record holds.");

        Provenance workspaceLater = Fixture(web: new WebRecord("custom", [new WebApp("Site", "vite-react", "public", "spa")], []));
        expect(PlanOf(workspaceLater).Replays.Count == 1, "A first app recorded under another scope than generation gives was generated rather than replayed.");
    }

    private static void RefusalCases(string scratch, Action<bool, string> expect)
    {
        string root = Path.Combine(scratch, "refusals");
        string feed = Path.Combine(scratch, "feed");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(feed);

        foreach (string version in new[] { "2.0.0", "2.1.0" })
        {
            using ZipArchive archive = ZipFile.Open(Path.Combine(feed, $"P.{version}.nupkg"), ZipArchiveMode.Create);
            using var writer = new StreamWriter(archive.CreateEntry("P.nuspec").Open());
            writer.Write($"<package><metadata><id>P</id><version>{version}</version></metadata></package>");
        }

        string Refusal(params string[] arguments)
        {
            TextWriter output = Console.Out;
            TextWriter error = Console.Error;
            using var refusal = new StringWriter();
            Console.SetOut(TextWriter.Null);
            Console.SetError(refusal);

            try
            {
                return Run(arguments) == 1 ? refusal.ToString() : "(accepted)";
            }
            finally
            {
                Console.SetOut(output);
                Console.SetError(error);
            }
        }

        void Refused(string because, string expected, params string[] arguments)
        {
            string refusal = Refusal(arguments);
            expect(refusal.Contains(expected, StringComparison.Ordinal), $"{because} was not refused with '{expected}': {refusal.Trim()}");
        }

        Refused("A project without cleanarch.json", "holds no cleanarch.json", "--root", root, "--source", feed);

        File.WriteAllText(Path.Combine(root, _projectFileName), Fixture(version: "2.1.0").Json.ToJsonString(_written));
        Refused("A project at the latest version", "No stable version of P is newer than 2.1.0", "--root", root, "--source", feed);
        Refused("An older target", "is not newer than 2.1.0", "--root", root, "--source", feed, "--to", "2.0.0");
        Refused("A missing target version", "Version 9.9.9 of P is not found", "--root", root, "--source", feed, "--to", "9.9.9");
        Refused("A project version no source holds", "Version 2.1.0 of P, the one cleanarch.json records, is not found", "--root", root, "--source", Path.Combine(feed, "P.2.0.0.nupkg"));
        Refused("A missing source", "is neither a .nupkg nor a folder", "--root", root, "--source", Path.Combine(feed, "none"));
        Refused("A JSON report inside the project", "is inside the project", "--root", root, "--source", feed, "--json", Path.Combine(root, "report.json"));
        expect(!File.Exists(Path.Combine(root, "report.json")), "A refused JSON report was written in the project.");
        Refused("Generations kept inside the project", "is inside the project", "--root", root, "--source", feed, "--keep", Path.Combine(root, "kept"));
        expect(!Directory.Exists(Path.Combine(root, "kept")), "A refused --keep folder was created in the project.");
        Refused("An unknown option", "is not an option of upgrade-report", "--root", root, "--unknown", "x");

        Directory.CreateDirectory(Path.Combine(root, ".template.config"));
        File.WriteAllText(Path.Combine(root, ".template.config", "template.json"), "{}");
        Refused("The package's own checkout", "is the checkout the package is packed from", "--root", root, "--source", feed);
    }

    private static void PackageCases(string scratch, Action<bool, string> expect)
    {
        PackageContent Packed(string name, string manifest, string project)
        {
            string nupkg = Path.Combine(scratch, name + ".nupkg");

            using (ZipArchive archive = ZipFile.Open(nupkg, ZipArchiveMode.Create))
            {
                foreach ((string path, string text) in new[]
                {
                    ("P.nuspec", "<package><metadata><id>P</id><version>1.0.0</version></metadata></package>"),
                    ("content/.template.config/template.json", manifest),
                    ("content/Src/A/A.csproj", project),
                })
                {
                    using var writer = new StreamWriter(archive.CreateEntry(path).Open());
                    writer.Write(text);
                }
            }

            return PackageContent.Read(nupkg);
        }

        PackageContent drawing = Packed(
            "drawing",
            "{ \"identity\": \"P\", \"guids\": [\"11111111-2222-3333-4444-555555555555\"] }",
            "<UserSecretsId>11111111-2222-3333-4444-555555555555</UserSecretsId><Type>{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}</Type>");
        PackageContent plain = Packed("plain", "{ \"identity\": \"P\" }", "<Type>{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}</Type>");

        expect(
            drawing.DrawsGuids && drawing.HeldGuids.Contains("fae04ec0-301f-11d3-bf4b-00c04f79efbc") && drawing.HeldGuids.Contains("11111111-2222-3333-4444-555555555555"),
            $"The GUIDs a package holds, or the guids its template declares, were not read: [{string.Join(", ", drawing.HeldGuids)}].");
        expect(!plain.DrawsGuids, "A template that declares no guids was read as drawing some.");
    }

    private static void ChangelogCases(Action<bool, string> expect)
    {
        string changelog = "# Changelog\n\nIntro.\n\n## [2.2.0](https://example.invalid) (2026-12-01)\n\n* c\n\n## 2.1.0 (2026-11-20)\n\n* b\n\n## 2.0.0 (2026-11-10)\n\n* a\n";
        string between = ChangelogBetween(changelog, "2.0.0", "2.1.0");

        expect(between.Contains("* b", StringComparison.Ordinal) && !between.Contains("* a", StringComparison.Ordinal) && !between.Contains("* c", StringComparison.Ordinal) && !between.Contains("Intro", StringComparison.Ordinal),
            "The changelog entries are not exactly those after the project's version up to the target: " + between);
        expect(ChangelogBetween(changelog, "2.2.0", "2.3.0").StartsWith("CHANGELOG.md holds no entry", StringComparison.Ordinal), "An empty range is not said to be empty.");
    }

    private static Provenance Fixture(
        string version = "2.0.0",
        (string Key, JsonNode Value)[]? options = null,
        WebRecord? web = null,
        (string Name, string Framework)[]? presentation = null)
    {
        var json = new JsonObject
        {
            ["template"] = new JsonObject { ["package"] = "P", ["version"] = version },
            ["name"] = "Sample.Shop",
            ["options"] = new JsonObject((options ?? []).Select(option => KeyValuePair.Create(option.Key, (JsonNode?)option.Value.DeepClone()))),
        };

        return new Provenance("P", version, "Sample.Shop", options ?? [], web, presentation ?? [("Api", "api")], json);
    }

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }
}

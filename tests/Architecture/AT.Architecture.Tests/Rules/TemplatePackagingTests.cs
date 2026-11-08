using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class TemplatePackagingTests
{
    private static readonly string[] _projectTypeGuids =
    [
        "FAE04EC0-301F-11D3-BF4B-00C04F79EFBC",
        "2150E333-8FDC-42A3-9474-1A3956D46DE8",
    ];

    private static readonly string[] _projectRoots = ["src", "tests"];

    private static readonly string[] _recordedAppKeys = ["framework", "audience", "rendering", "port"];

    private static readonly byte[] _utf8Bom = [0xEF, 0xBB, 0xBF];

    private const string _repositoryOwner = "Pierre-MarieMarchio";

    private const string _repositoryName = "cleanarch-dotnet";

    private const string _repositoryUrl = $"https://github.com/{_repositoryOwner}/{_repositoryName}";

    private static readonly string[] _packageProjects =
    [
        ".template.config/TemplatePackage.csproj",
        "launcher/CleanArch.Create/CleanArch.Create.csproj",
        "launcher/CleanArch.Cli/CleanArch.Cli.csproj",
    ];

    private const string _nugetOrgReadmeRules = "https://learn.microsoft.com/en-us/nuget/nuget-org/package-readme-on-nuget-org";

    private static readonly string[] _nugetOrgImageHosts =
    [
        "api.codacy.com", "api.codeclimate.com", "api.dependabot.com", "api.reuse.software", "api.travis-ci.com",
        "app.codacy.com", "app.deepsource.com", "avatars.githubusercontent.com", "badgen.net", "badges.gitter.im",
        "camo.githubusercontent.com", "caniuse.bitsofco.de", "cdn.jsdelivr.net", "cdn.syncfusion.com", "ci.appveyor.com",
        "circleci.com", "cloudback.it", "codecov.io", "codefactor.io", "coveralls.io", "dev.azure.com", "devpod.sh",
        "flat.badgen.net", "gitlab.com", "i.imgur.com", "img.shields.io", "infragistics.com", "isitmaintained.com",
        "media.githubusercontent.com", "opencollective.com", "raw.github.com", "raw.githubusercontent.com", "snyk.io",
        "sonarcloud.io", "travis-ci.com", "travis-ci.org", "user-images.githubusercontent.com",
    ];

    private static readonly Regex _workflowBadge = new(@"^/[^/]+/[^/]+/(?:actions/)?workflows/[^/]+/badge\.svg$", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static readonly Regex _fencedBlock = new(@"^(```|~~~).*?^\1", RegexOptions.Multiline | RegexOptions.Singleline, TimeSpan.FromSeconds(5));

    private static readonly Regex _codeSpan = new("(`+).+?\\1", RegexOptions.Singleline, TimeSpan.FromSeconds(5));

    private static readonly Regex _htmlTag = new(@"<(?!https?://|mailto:)[A-Za-z!/][^>]*>", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static readonly Regex _linkTarget = new(
        @"\]\(\s*<?(?<url>[^)\s>]+)|^\s{0,3}\[[^\]]+\]:\s*<?(?<url>[^\s>]+)",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    private static readonly Regex _imageTarget = new(@"!\[[^\]]*\]\(\s*<?(?<url>[^)\s>]+)", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static readonly Regex _ownersRepository = new(
        $@"github\.com[/:]{_repositoryOwner}/(?<repository>[A-Za-z0-9_.-]+?)(?:\.git)?(?=[/)\]\s""'<>#?`]|$)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline,
        TimeSpan.FromSeconds(5));

    private static readonly Regex _guid = new(
        "[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex _markerLine = new(
        "^\\s*<!--" + "#" + "(?:if|else|elseif|endif)\\b.*-->\\s*$",
        RegexOptions.None,
        TimeSpan.FromSeconds(5));

    private static readonly Regex _tableRow = new("^\\s*\\|", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static readonly Regex _fence = new("^\\s*(?:```|~~~)", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static readonly string[] _markdownSkippedFolders = [".git", ".claude", "bin", "obj", "node_modules", "TestResults", "artifacts", "plans"];

    [Fact]
    public void NoMarkerOnALineOfItsOwn_EndsAMarkdownTable()
    {
        var offenders = MarkerLinesEndingATable().Select(offender => $"{offender.File}:{offender.Line}");

        offenders.ShouldBeEmpty(
            "A template marker on a line of its own between two table rows is an HTML block, which ends the table GitHub " +
            "renders: every row after it shows as plain text with its pipes. Write the marker as a row of one cell, " +
            "'| <marker> |', which generation removes whole as it removes the line.");
    }

    [Fact]
    public void AMarkerLineBetweenTwoRows_IsFound_AndARowMarkerOrAFencedMarkerIsNot()
    {
        string opening = "<!--" + "#" + "if (files)-->";
        string closing = "<!--" + "#" + "endif-->";
        string[] lines =
        [
            "| Name | Value |",
            "|---|---|",
            opening,
            "| a | 1 |",
            closing,
            "| b | 2 |",
            $"| {opening} |",
            "| c | 3 |",
            $"| {closing} |",
            "| d | 4 |",
            "",
            "```markdown",
            "| e | 5 |",
            opening,
            "| f | 6 |",
            "```",
            "",
            opening,
            "A sentence.",
            closing,
        ];

        MarkerLinesBetweenRows(lines).ShouldBe([3, 5]);
    }

    [Fact]
    public void NoMarkerInAnIndentedFence_SitsLeftOfTheFence()
    {
        var offenders = MarkdownLinesWhere(MarkerLinesLeftOfTheirFence).Select(offender => $"{offender.File}:{offender.Line}");

        offenders.ShouldBeEmpty(
            "A template marker written left of the fence it sits in ends the list item the fence is indented under, " +
            "which closes the fence on GitHub and renders the rest of the page out of place. Indent the marker to the " +
            "column of its fence, which generation removes whole as it removes any marker line.");
    }

    [Fact]
    public void AMarkerLeftOfAnIndentedFence_IsFound_AndAnIndentedOrAColumnZeroFenceMarkerIsNot()
    {
        string opening = "<!--" + "#" + "if (files)-->";
        string closing = "<!--" + "#" + "endif-->";
        string[] lines =
        [
            "- an item:",
            "",
            "  ```bash",
            opening,
            "  run",
            $"  {closing}",
            $"   {opening}",
            "  ```",
            "",
            "```bash",
            opening,
            "run",
            closing,
            "```",
            opening,
        ];

        MarkerLinesLeftOfTheirFence(lines).ShouldBe([4]);
    }

    private static IEnumerable<(string File, int Line)> MarkerLinesEndingATable() => MarkdownLinesWhere(MarkerLinesBetweenRows);

    private static IEnumerable<(string File, int Line)> MarkdownLinesWhere(Func<string[], List<int>> offendingLines)
    {
        string root = ProjectReferenceGraph.RepositoryRoot;

        return new DirectoryInfo(root)
            .EnumerateFiles("*.md", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
            .Select(file => Path.GetRelativePath(root, file.FullName))
            .Where(file => !file.Split(Path.DirectorySeparatorChar).SkipLast(1).Any(_markdownSkippedFolders.Contains))
            .SelectMany(file => offendingLines(File.ReadAllLines(Path.Combine(root, file)))
                .Select(line => (File: file.Replace('\\', '/'), Line: line)))
            .Order();
    }

    private static List<int> MarkerLinesLeftOfTheirFence(string[] lines)
    {
        var found = new List<int>();
        int? fenceColumn = null;
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index];
            if (_fence.IsMatch(line))
            {
                fenceColumn = fenceColumn is null ? line.Length - line.TrimStart().Length : null;
            }
            else if (fenceColumn is not null && _markerLine.IsMatch(line) && line.Length - line.TrimStart().Length < fenceColumn)
            {
                found.Add(index + 1);
            }
        }

        return found;
    }

    private static List<int> MarkerLinesBetweenRows(string[] lines)
    {
        bool[] fenced = new bool[lines.Length];
        bool inFence = false;
        for (int index = 0; index < lines.Length; index++)
        {
            bool fence = _fence.IsMatch(lines[index]);
            fenced[index] = inFence || fence;
            inFence ^= fence;
        }

        bool IsMarker(int index) => !fenced[index] && _markerLine.IsMatch(lines[index]);
        bool IsRow(int index) => index >= 0 && index < lines.Length && !fenced[index] && _tableRow.IsMatch(lines[index]);

        var found = new List<int>();
        int start = 0;
        while (start < lines.Length)
        {
            if (!IsMarker(start))
            {
                start++;
                continue;
            }

            int end = start;
            while (end < lines.Length && IsMarker(end))
            {
                end++;
            }

            if (IsRow(start - 1) && IsRow(end))
            {
                found.AddRange(Enumerable.Range(start + 1, end - start));
            }

            start = end;
        }

        return found;
    }

    [Fact]
    public void EveryGuidInTheSolution_IsRegeneratedOnGeneration()
    {
        var declared = DeclaredGuids();
        SolutionGuids()
            .Except(declared, StringComparer.OrdinalIgnoreCase)
            .Except(_projectTypeGuids, StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ShouldBeEmpty(
                "A guid in the solution is not in the template manifest's 'guids' list, so two " +
                "projects generated from this template would share it. Add it there.");
    }

    [Fact]
    public void NoDeclaredGuid_IsAbsentFromTheSolution()
    {
        var declared = DeclaredGuids();

        declared.ShouldNotBeEmpty("The manifest declares no guids, which cannot be right.");

        declared
            .Except(SolutionGuids(), StringComparer.OrdinalIgnoreCase)
            .Except(UserSecretsGuids().Select(secrets => secrets.Guid), StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.Ordinal)
            .ShouldBeEmpty(
                "The template manifest lists a guid neither the solution nor a UserSecretsId contains. It was " +
                "left behind by a removed project, and a stale list is one nobody trusts.");
    }

    [Fact]
    public void EveryUserSecretsId_IsRegeneratedOnGeneration()
    {
        var declared = DeclaredGuids();
        var secrets = UserSecretsGuids();

        secrets.ShouldNotBeEmpty("The API's project declares a UserSecretsId; a search that finds none is broken.");

        secrets
            .Where(secret => !declared.Contains(secret.Guid, StringComparer.OrdinalIgnoreCase))
            .Select(secret => $"{secret.Project}: {secret.Guid}")
            .ShouldBeEmpty(
                "A UserSecretsId the template writes is not in the manifest's 'guids' list, so every project " +
                "generated from this template would read and write the same secrets on a machine, the signing key " +
                "among them. Add it there.");
    }

    [Fact]
    public void TheProjectTypeGuids_AreNotRegenerated()
    {
        var declared = DeclaredGuids();

        foreach (string typeGuid in _projectTypeGuids)
        {
            declared.ShouldNotContain(
                typeGuid,
                StringComparer.OrdinalIgnoreCase,
                $"{typeGuid} identifies a kind of project, not an instance of one.");
        }
    }

    [Fact]
    public void EveryStorageProjectLine_InTheSolution_IsConditionedOnFiles() =>
        AssertEveryProjectLineIsConditioned(".Storage", "files", expectedProjects: 2);

    [Fact]
    public void EveryAuthProjectLine_InTheSolution_IsConditionedOnLocalAuth() =>
        AssertEveryProjectLineIsConditioned(".Auth", "localAuth", expectedProjects: 5);

    [Fact]
    public void EveryClientProjectLine_InTheSolution_IsConditionedOnDotnetClient() =>
        AssertEveryProjectLineIsConditioned(".Client", "dotnetClient", expectedProjects: 2);

    private static void AssertEveryProjectLineIsConditioned(string marker, string symbol, int expectedProjects)
    {
        string opening = "<!--" + "#" + $"if ({symbol})-->";
        string closing = "<!--" + "#" + "endif-->";

        string[] lines = [.. File.ReadAllLines(Path.Combine(ProjectReferenceGraph.RepositoryRoot, "AppTemplate.slnx")).Select(line => line.Trim())];
        bool NamesTheProject(string line) => line.StartsWith("<Project ", StringComparison.Ordinal) && line.Contains(marker, StringComparison.Ordinal);

        lines.Count(NamesTheProject).ShouldBe(expectedProjects, $"{expectedProjects} projects named '{marker}' were expected in the solution.");

        var unconditioned = new List<string>();
        bool inBlock = false;
        foreach (string line in lines)
        {
            if (line == opening)
            {
                inBlock = true;
            }
            else if (line == closing)
            {
                inBlock = false;
            }
            else if (!inBlock && NamesTheProject(line))
            {
                unconditioned.Add(line);
            }
        }

        unconditioned.ShouldBeEmpty($"These solution lines name a '{marker}' project outside a block opened by '{opening}'.");
    }

    [Fact]
    public void TheGlobalJsonWithoutTheAppHost_IsTheCheckoutsMinusItsMsBuildSdks()
    {
        string root = ProjectReferenceGraph.RepositoryRoot;
        var full = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "global.json")))!;
        var withoutAppHost = JsonNode.Parse(File.ReadAllText(
            Path.Combine(root, ".template.config", "variants", "no-aspire", "global.json")))!;

        full.AsObject().Remove("msbuild-sdks").ShouldBeTrue("global.json no longer pins the app host's SDK under msbuild-sdks.");

        JsonNode.DeepEquals(full, withoutAppHost).ShouldBeTrue(
            ".template.config/variants/no-aspire/global.json has drifted from global.json.");
    }

    [Fact]
    public void ThePackageTemplatesTools_AreAtTheWorkspacesVersions()
    {
        var pinned = WorkspacePackagesPinnedTools();
        var template = DevDependencies(Path.Combine(ProjectReferenceGraph.RepositoryRoot, ".template.config", "webpackage", "content", "placeholder-package", "package.json"))
            .Where(dependency => pinned.Contains(dependency.Name))
            .ToList();

        template.ShouldNotBeEmpty("The package template shares no pinned tool with the workspace's packages, so this rule reads the wrong files.");

        Drifted(template.Select(dependency => (".template.config/webpackage", dependency)), pinned)
            .ShouldBeEmpty(
                "cleanarch-webpackage pins a tool at another version than the workspace's packages, which Dependabot moves without it: "
                + "two Vitest versions in one workspace clear each other's shared module cache while pnpm runs them side by side.");
    }

    [Fact]
    public void EachScaffoldsTools_AreAtTheWorkspacesVersions()
    {
        var pinned = WorkspacePackagesPinnedTools();
        var scaffolds = Directory.EnumerateFiles(StartersFolder, "scaffold.json", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("\"package.json\"", StringComparison.Ordinal))
            .SelectMany(path => ScaffoldDevDependencies(path)
                .Where(dependency => pinned.Contains(dependency.Name))
                .Select(dependency => (Path.GetRelativePath(ProjectReferenceGraph.RepositoryRoot, path), dependency)))
            .ToList();

        scaffolds.ShouldNotBeEmpty("No scaffold.json merges a tool the workspace's packages pin, so this rule reads the wrong files.");

        Drifted(scaffolds, pinned)
            .ShouldBeEmpty(
                "A web starter's scaffold.json pins a tool at another version than the workspace's packages, which Dependabot moves without it: "
                + "two Vitest versions in one workspace clear each other's shared module cache while pnpm runs them side by side.");
    }

    private static ILookup<string, string> WorkspacePackagesPinnedTools() =>
        Directory.EnumerateFiles(Path.Combine(ProjectReferenceGraph.RepositoryRoot, "src", "Web", "packages"), "package.json", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(path => DevDependencies(path))
            .Where(dependency => dependency.Version.Length > 0 && char.IsDigit(dependency.Version[0]))
            .ToLookup(dependency => dependency.Name, dependency => dependency.Version, StringComparer.Ordinal);

    private static IEnumerable<string> Drifted(IEnumerable<(string File, (string Name, string Version) Dependency)> pins, ILookup<string, string> pinned) =>
        pins
            .Where(pin => !pinned[pin.Dependency.Name].All(version => version == pin.Dependency.Version))
            .Select(pin => $"{pin.File}: {pin.Dependency.Name} {pin.Dependency.Version}, the workspace: {string.Join(", ", pinned[pin.Dependency.Name].Distinct(StringComparer.Ordinal))}");

    private static IEnumerable<(string Name, string Version)> DevDependencies(string packageJson) =>
        DevDependencies(JsonNode.Parse(File.ReadAllText(packageJson)));

    private static IEnumerable<(string Name, string Version)> ScaffoldDevDependencies(string scaffoldJson) =>
        DevDependencies(JsonNode.Parse(File.ReadAllText(scaffoldJson))?["merge"]?["package.json"]);

    private static IEnumerable<(string Name, string Version)> DevDependencies(JsonNode? packageJson) =>
        packageJson?["devDependencies"]?.AsObject()
            .Where(pair => pair.Value is not null)
            .Select(pair => (pair.Key, pair.Value!.GetValue<string>()))
        ?? [];

    [Theory]
    [InlineData(".config", new[] { "cleanarch.cli" })]
    [InlineData(".template.config/variants/no-sonar/.config", new[] { "dotnet-sonarscanner" })]
    [InlineData(".template.config/variants/no-client/.config", new[] { "microsoft.openapi.kiota" })]
    [InlineData(".template.config/variants/no-sonar-no-client/.config", new[] { "dotnet-sonarscanner", "microsoft.openapi.kiota" })]
    public void EachToolManifest_IsTheGeneratedProjectsMinusItsTools(string folder, string[] removed)
    {
        JsonNode full = JsonNode.Parse(File.ReadAllText(GeneratedToolManifestPath()))!;
        JsonNode withoutTools = JsonNode.Parse(File.ReadAllText(Path.Combine(ProjectReferenceGraph.RepositoryRoot, folder, "dotnet-tools.json")))!;

        foreach (string tool in removed)
        {
            full["tools"]!.AsObject().Remove(tool).ShouldBeTrue($"The generated project's tool manifest no longer declares {tool}.");
        }

        JsonNode.DeepEquals(full, withoutTools).ShouldBeTrue(
            $"{folder}/dotnet-tools.json has drifted from .template.config/variants/project/.config/dotnet-tools.json, the one a generated project receives.");
    }

    [Fact]
    public void TheWindowsMatcherOfTheGeneratedRule_IsTheOneTheToolReadsTestProjectsWith()
    {
        string root = ProjectReferenceGraph.RepositoryRoot;
        var verbatimPattern = new Regex(@"new\(\s*@""(?<pattern>(?:[^""]|"""")*)""", RegexOptions.None, TimeSpan.FromSeconds(2));
        string Pattern(string file) => verbatimPattern.Match(File.ReadAllText(Path.Combine(root, file))).Groups["pattern"].Value;

        string tool = Pattern("launcher/CleanArch.Cli.Commands/Kernel/TestProjects.cs");

        tool.ShouldContain("-windows", Case.Sensitive, "TestProjects.cs no longer declares the tool's Windows matcher first, so this rule compares nothing.");
        Pattern("tests/Architecture/AT.Architecture.Tests/Rules/WindowsTestProjectTests.cs").ShouldBe(
            tool,
            "WindowsTestProjectTests holds a Windows matcher other than the one dotnet cleanarch leaves a Windows test project out with.");
    }

    [Fact]
    public void TheGeneratedProjectsToolManifest_PinsTheProjectsToolAtTheTemplatesVersion()
    {
        XDocument tool = XDocument.Load(Path.Combine(ProjectReferenceGraph.RepositoryRoot, "launcher", "CleanArch.Cli", "CleanArch.Cli.csproj"));
        using var manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        JsonNode pinned = JsonNode.Parse(File.ReadAllText(GeneratedToolManifestPath()))!["tools"]![tool.Descendants("PackageId").Single().Value.ToLowerInvariant()]
            .ShouldNotBeNull("The generated project's tool manifest does not pin CleanArch.Cli, so dotnet tool restore leaves dotnet cleanarch out.");

        pinned["version"]!.GetValue<string>().ShouldBe(
            Text(manifest.RootElement.GetProperty("symbols").GetProperty("templateVersion"), "replaces"),
            "CleanArch.Cli is pinned at another version than the token templateVersion replaces, so a generated project restores a tool of another release.");
        pinned["commands"]!.AsArray().Select(command => command!.GetValue<string>()).ShouldBe([tool.Descendants("ToolCommandName").Single().Value]);
    }

    private static string GeneratedToolManifestPath() =>
        Path.Combine(ProjectReferenceGraph.RepositoryRoot, ".template.config", "variants", "project", ".config", "dotnet-tools.json");

    [Fact]
    public void TheCheckoutsProjectFile_RecordsWhatTheDefaultGenerationWrites()
    {
        string root = ProjectReferenceGraph.RepositoryRoot;
        var projectFile = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "cleanarch.json")))!;
        using var manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        JsonElement symbols = manifest.RootElement.GetProperty("symbols");
        var options = new JsonObject();

        foreach (JsonProperty option in RecordedOptions(symbols))
        {
            string value = option.Value.GetProperty("defaultValue").GetString()!;
            options[option.Name] = Text(option.Value, "datatype") == "bool" ? JsonValue.Create(bool.Parse(value)) : JsonValue.Create(value);
        }

        var expected = new JsonObject
        {
            ["template"] = new JsonObject
            {
                ["package"] = XDocument.Load(Path.Combine(root, ".template.config", "TemplatePackage.csproj")).Descendants("PackageId").Single().Value,
                ["version"] = Text(symbols.GetProperty("templateVersion"), "replaces"),
            },
            ["name"] = manifest.RootElement.GetProperty("sourceName").GetString(),
            ["options"] = options,
            ["client"] = false,
            ["presentation"] = new JsonArray(new JsonObject { ["name"] = "Api", ["framework"] = "api" }),
        };

        JsonNode.DeepEquals(projectFile, expected).ShouldBeTrue(
            $"cleanarch.json is the file the defaults generate, {expected.ToJsonString()}, with every option of template.json that is not a part, under its default; it holds {projectFile.ToJsonString()}.");
    }

    [Theory]
    [InlineData("project-file-web", new[] { "web" })]
    public void EachProjectFileVariant_IsTheCheckoutsPlusItsParts(string variant, string[] parts)
    {
        string root = ProjectReferenceGraph.RepositoryRoot;
        var withParts = JsonNode.Parse(File.ReadAllText(Path.Combine(root, ".template.config", "variants", variant, "cleanarch.json")))!.AsObject();
        var checkout = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "cleanarch.json")))!;

        withParts.Select(property => property.Key).Except(checkout.AsObject().Select(property => property.Key)).ShouldBe(
            parts,
            Case.Sensitive,
            $".template.config/variants/{variant}/cleanarch.json adds other keys than {string.Join(" and ", parts)} to cleanarch.json, or in another order.");

        foreach (string part in parts)
        {
            withParts.Remove(part);
        }

        JsonNode.DeepEquals(checkout, withParts).ShouldBeTrue($".template.config/variants/{variant}/cleanarch.json has drifted from cleanarch.json.");
    }

    [Fact]
    public void EveryValueTheProjectFileRecords_IsReplacedAtGeneration()
    {
        string root = ProjectReferenceGraph.RepositoryRoot;
        var withParts = JsonNode.Parse(File.ReadAllText(
            Path.Combine(root, ".template.config", "variants", "project-file-web", "cleanarch.json")))!;
        using var manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        JsonElement symbols = manifest.RootElement.GetProperty("symbols");
        string sourceName = manifest.RootElement.GetProperty("sourceName").GetString()!;
        string[] replaced = [.. symbols.EnumerateObject().Select(symbol => Text(symbol.Value, "replaces")).OfType<string>(), sourceName];
        string Line(JsonNode? owner, string key) => $"\"{key}\": {owner![key]!.ToJsonString()}";
        JsonNode api = withParts["presentation"]![0]!;

        string[] recorded =
        [
            .. RecordedOptions(symbols).Select(option => Line(withParts["options"], option.Name)),
            Line(withParts, "client"),
            .. _recordedAppKeys.Select(key => Line(withParts["web"]!["apps"]![0], key)),
            $"{{\n      {Line(api, "name")},\n      {Line(api, "framework")}\n    }}",
            withParts["name"]!.GetValue<string>(),
            withParts["template"]!["version"]!.GetValue<string>(),
            withParts["web"]!["scope"]!.GetValue<string>(),
            withParts["web"]!["apps"]![0]!["name"]!.GetValue<string>(),
        ];

        recorded.Where(token => !replaced.Contains(token, StringComparer.Ordinal)).ShouldBeEmpty(
            "No symbol of template.json replaces these values of the cleanarch.json files, so every project would record the checkout's value.");
    }

    [Fact]
    public void TheDependabotEntriesTheWebReadmeLists_AreTheOnesWebWrites()
    {
        string root = ProjectReferenceGraph.RepositoryRoot;
        var written = ConditionedBlocks(File.ReadAllLines(Path.Combine(root, ".github", "dependabot.yml")), "anyWeb");
        var listed = AddedBlocksUnder(File.ReadAllLines(Path.Combine(root, "src", "Web", "README.md")), "- in `.github/dependabot.yml`");

        written.Count.ShouldBe(2, "dependabot.yml was expected to hold two blocks conditioned on anyWeb: the apps' images and the workspace's packages.");

        listed.ShouldBe(
            written,
            "src/Web/README.md lists Dependabot entries for a project's first web app that differ from what --web writes " +
            "into .github/dependabot.yml. Copy the anyWeb blocks into the README, as they are.");
    }

    [Fact]
    public void ThePackage_LeavesOutWhatGenerationAlwaysLeavesOut()
    {
        var packageExcludes = PackageExcludes();
        using var manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        var modifiers = manifest.RootElement.GetProperty("sources")[0].GetProperty("modifiers").EnumerateArray().ToList();

        var alwaysExcluded = modifiers
            .Where(modifier => !modifier.TryGetProperty("condition", out var condition) || condition.GetString() == "(!templateMaintenance)")
            .SelectMany(modifier => modifier.GetProperty("exclude").EnumerateArray().Select(pattern => pattern.GetString()!))
            .Where(pattern => pattern != ".template.config/**")
            .ToList();

        alwaysExcluded.Count.ShouldBeGreaterThanOrEqualTo(20, "Far fewer unconditional exclusions were read from template.json than it declares.");

        alwaysExcluded
            .Where(pattern => !packageExcludes.Any(exclude =>
                exclude == pattern || (exclude.StartsWith("**/", StringComparison.Ordinal) && pattern.EndsWith(exclude[3..], StringComparison.Ordinal))))
            .ShouldBeEmpty(
                "template.json never generates these paths, but .template.config/TemplatePackage.csproj packs them: add each to its TemplateExclude items.");
    }

    [Fact]
    public void ATestSourceThePackageLeavesOut_GenerationLeavesOutToo()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        var generationExcludes = manifest.RootElement.GetProperty("sources")[0].GetProperty("modifiers").EnumerateArray()
            .Where(modifier => modifier.TryGetProperty("condition", out var condition) && condition.GetString() == "(!templateMaintenance)")
            .SelectMany(modifier => modifier.GetProperty("exclude").EnumerateArray().Select(pattern => pattern.GetString()!))
            .ToHashSet(StringComparer.Ordinal);

        var testSources = PackageExcludes()
            .Where(exclude => exclude.StartsWith("tests/", StringComparison.Ordinal) && exclude.EndsWith(".cs", StringComparison.Ordinal))
            .ToList();

        testSources.ShouldNotBeEmpty("No test source was read from the TemplateExclude items, so this rule is reading the wrong file.");

        testSources
            .Where(source => !generationExcludes.Contains(source))
            .ShouldBeEmpty(
                "The package leaves out these test sources, but a template installed from the checkout generates them, without the " +
                "template-only fixtures they read: add each to the (!templateMaintenance) exclusions of template.json.");
    }

    [Fact]
    public void ThePackagesMetadataFiles_AreAtItsRootAndNotAlsoUnderContent()
    {
        string project = Path.Combine(ProjectReferenceGraph.RepositoryRoot, ".template.config", "TemplatePackage.csproj");
        var packageExcludes = PackageExcludes();

        var metadata = XDocument.Load(project)
            .Descendants("None")
            .Where(item => (string?)item.Attribute("PackagePath") == "/")
            .Select(item => item.Attribute("Include")!.Value
                .Replace("$(TemplateRoot)", string.Empty, StringComparison.Ordinal)
                .Replace("$(MSBuildThisFileDirectory)", ".template.config/", StringComparison.Ordinal))
            .ToList();

        metadata.ShouldContain(".template.config/icon.png", "The package no longer packs its icon at its root.");
        metadata
            .Where(file => !packageExcludes.Any(exclude => exclude == file || (exclude.EndsWith("/**", StringComparison.Ordinal) && file.StartsWith(exclude[..^2], StringComparison.Ordinal))))
            .ShouldBeEmpty(
                "These metadata files sit at the package's root and, with the content glob, under content/ as well: add each to the TemplateExclude items of .template.config/TemplatePackage.csproj.");
    }

    [Fact]
    public void ThePullRequestTemplate_ReachesEveryGeneratedProject()
    {
        string[] templatePaths = [".github/pull_request_template.md", ".github/**"];
        using var manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath()));

        var generationExcludes = manifest.RootElement.GetProperty("sources")[0].GetProperty("modifiers").EnumerateArray()
            .SelectMany(modifier => modifier.GetProperty("exclude").EnumerateArray().Select(pattern => pattern.GetString()!));

        generationExcludes.Intersect(templatePaths).ShouldBeEmpty(
            "template.json leaves the pull request template out of a generated project, which carries the authorship rule.");
        PackageExcludes().Any(exclude => templatePaths.Any(path => exclude.EndsWith(path, StringComparison.Ordinal))).ShouldBeFalse(
            ".template.config/TemplatePackage.csproj leaves the pull request template out of the package.");
    }

    [Fact]
    public void EveryGeneratedProject_ReceivesItsOwnIssueFormsAndPullRequestTemplate()
    {
        string root = ProjectReferenceGraph.RepositoryRoot;
        string variant = Path.Combine(root, ".template.config", "variants", "project", ".github");

        IssueFormTests.AssertForms(Path.Combine(variant, "ISSUE_TEMPLATE"));
        PullRequestTemplateTests.AssertCarriesTheAuthorshipRule(Path.Combine(variant, "pull_request_template.md"));

        var files = Directory.EnumerateFiles(variant, "*", SearchOption.AllDirectories)
            .Select(file => File.ReadAllText(file))
            .ToList();
        files.ShouldNotBeEmpty("The project's .github variant holds no file.");
        files.Where(text => text.Contains("dotnet new", StringComparison.Ordinal) || text.Contains("template", StringComparison.OrdinalIgnoreCase))
            .ShouldBeEmpty("A file generated into a project talks about the template: it is written for the project.");
    }

    [Fact]
    public void TheTemplatesCommunityFiles_ReachNeitherAProjectNorTheTemplatePackage()
    {
        string[] files = ["CODE_OF_CONDUCT.md", "SUPPORT.md", ".github/FUNDING.yml"];

        foreach (string file in files)
        {
            File.Exists(Path.Combine(ProjectReferenceGraph.RepositoryRoot, file)).ShouldBeTrue($"{file} was not found.");
        }

        files.Except(UnconditionalExcludes(), StringComparer.Ordinal).ShouldBeEmpty(
            "template.json generates files that are about this repository alone: add each to its unconditional excludes.");
        files.Except(PackageExcludes(), StringComparer.Ordinal).ShouldBeEmpty(
            "The template package carries files that are about this repository alone: add each to the TemplateExclude items of .template.config/TemplatePackage.csproj.");
    }

    [Fact]
    public void ThePackage_LeavesOutEveryScratchAndMaintenancePath()
    {
        string[] scratch =
        [
            ".git/**", ".claude/**", ".vs/**", "**/bin/**", "**/obj/**", "**/node_modules/**", "**/TestResults/**",
            "artifacts/**", ".template.config/TemplatePackage.csproj",
        ];

        scratch.Except(PackageExcludes(), StringComparer.Ordinal).ShouldBeEmpty(
            "A checkout can hold these paths, which the template package must not carry: add each to the TemplateExclude items of .template.config/TemplatePackage.csproj.");
    }

    [Fact]
    public void AFolderInstall_LeavesOutEveryLocalToolFolderThePackageLeavesOut()
    {
        var localToolFolders = PackageExcludes()
            .Where(pattern => Regex.IsMatch(pattern, @"^\.[A-Za-z]\w*/\*\*$", RegexOptions.None, TimeSpan.FromSeconds(5)))
            .ToList();

        localToolFolders.ShouldContain(".claude/**", "No dot folder was read from the TemplateExclude items, so this rule is reading the wrong file.");

        localToolFolders.Except(UnconditionalExcludes(), StringComparer.Ordinal).ShouldBeEmpty(
            ".template.config/TemplatePackage.csproj leaves these folders out of the package, but installing from a checkout copies them into every project: add each to the unconditional excludes of the main source of template.json.");
    }

    [Fact]
    public void TheLauncher_ReachesNeitherAProjectNorTheTemplatePackage()
    {
        const string launcher = "launcher/**";
        var alwaysExcluded = UnconditionalExcludes();

        Directory.Exists(Path.Combine(ProjectReferenceGraph.RepositoryRoot, "launcher")).ShouldBeTrue("The launcher's folder was not found.");
        alwaysExcluded.ShouldContain(launcher, "template.json generates the launcher into every project: add launcher/** to its unconditional excludes.");
        PackageExcludes().ShouldContain(launcher, "The template package carries the launcher, which is a package of its own: add launcher/** to the TemplateExclude items of .template.config/TemplatePackage.csproj.");
    }

    [Fact]
    public void TheCheckoutsMigrations_ReachNeitherAProjectNorTheTemplatePackage()
    {
        string[] migrations =
        [
            "src/Infrastructure/AT.Infrastructure.Persistence/Migrations/**",
            "src/Infrastructure/AT.Infrastructure.Auth/Migrations/**",
        ];

        migrations.Except(UnconditionalExcludes(), StringComparer.Ordinal).ShouldBeEmpty(
            "template.json generates the migrations bootstrap wrote for this checkout, which a project creates for its own model: add each to its unconditional excludes.");
        migrations.Except(PackageExcludes(), StringComparer.Ordinal).ShouldBeEmpty(
            "The template package carries the migrations bootstrap wrote for this checkout: add each to the TemplateExclude items of .template.config/TemplatePackage.csproj.");
    }

    [Fact]
    public void TheCheckoutsDocumentationIndex_ReachesNeitherAProjectNorTheTemplatePackage()
    {
        string[] indexes = ["docs/index.md", "docs/**/index.md"];

        File.Exists(Path.Combine(ProjectReferenceGraph.RepositoryRoot, "docs", "index.md")).ShouldBeTrue(
            "This checkout commits the documentation index docs-index writes, so the rule has a file to keep out.");
        indexes.Except(UnconditionalExcludes(), StringComparer.Ordinal).ShouldBeEmpty(
            "template.json generates the index of this checkout's pages, every branch's and the template's: add each to its unconditional excludes, since bootstrap writes a project's own.");
        indexes.Except(PackageExcludes(), StringComparer.Ordinal).ShouldBeEmpty(
            "The template package carries the index of this checkout's pages: add each to the TemplateExclude items of .template.config/TemplatePackage.csproj.");
    }

    [Fact]
    public void OnlyTheTemplateRepository_IgnoresTheMigrations()
    {
        const string migrations = "src/Infrastructure/*/Migrations/";
        string[] lines = File.ReadAllLines(Path.Combine(ProjectReferenceGraph.RepositoryRoot, ".gitignore"));

        lines.Count(line => line.Trim() == migrations).ShouldBe(1, $".gitignore was expected to name {migrations} once.");
        ConditionedBlocks(lines, "templateMaintenance").ShouldContain(
            block => block.Split('\n').Any(line => line.Trim() == migrations),
            $".gitignore ignores {migrations} outside a templateMaintenance block, so a generated project would never commit the migrations bootstrap creates.");
    }

    [Fact]
    public void TheOverview_RecordsThePackageAndTheVersionGenerationWrites()
    {
        string root = ProjectReferenceGraph.RepositoryRoot;
        string packageId = XDocument.Load(Path.Combine(root, ".template.config", "TemplatePackage.csproj"))
            .Descendants("PackageId").Single().Value;
        using var manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        string token = manifest.RootElement.GetProperty("symbols").GetProperty("templateVersion").GetProperty("replaces").GetString()!;
        string overview = File.ReadAllText(Path.Combine(root, "docs", "getting-started", "overview.md"));

        overview.ShouldContain($"version {token} of", Case.Sensitive, "docs/getting-started/overview.md no longer holds the token templateVersion replaces.");
        overview.ShouldContain($"`{packageId}`", Case.Sensitive, "docs/getting-started/overview.md no longer names the PackageId of .template.config/TemplatePackage.csproj.");
    }

    [Fact]
    public void EveryLinkToTheRepository_NamesItsCurrentName()
    {
        string root = ProjectReferenceGraph.RepositoryRoot;

        var links = RepositoryTextFiles(root)
            .SelectMany(file => _ownersRepository.Matches(File.ReadAllText(file))
                .Select(match => (File: Path.GetRelativePath(root, file), Link: match.Value, Repository: match.Groups["repository"].Value)))
            .ToList();

        links.Count.ShouldBeGreaterThanOrEqualTo(10, "Far fewer links to the repository were found than the tree holds, so this rule is reading the wrong files.");
        links
            .Where(link => !link.Repository.Equals(_repositoryName, StringComparison.OrdinalIgnoreCase))
            .Select(link => $"{link.File}: {link.Link}")
            .ShouldBeEmpty(
                $"A link names a repository of {_repositoryOwner} other than {_repositoryUrl}, where the template lives: " +
                "point it there. Only CHANGELOG.md keeps the links release-please wrote.");
    }

    [Fact]
    public void EveryPackage_NamesTheRepositoryAsItsSourceAndItsProject()
    {
        string root = ProjectReferenceGraph.RepositoryRoot;

        foreach (string package in _packageProjects)
        {
            var project = XDocument.Load(Path.Combine(root, package));
            project.Descendants("RepositoryUrl").Single().Value.ShouldBe(_repositoryUrl, $"{package} names another repository as its source.");
            project.Descendants("PackageProjectUrl").Single().Value.ShouldBe(_repositoryUrl, $"{package} names another repository as its project page.");
        }
    }

    [Fact]
    public void EveryPackageReadme_IsOneNugetOrgRenders()
    {
        string root = ProjectReferenceGraph.RepositoryRoot;

        foreach (string package in _packageProjects)
        {
            string readme = PackedReadme(root, package);
            NugetOrgRefusals(File.ReadAllText(readme)).ShouldBeEmpty(
                $"{Path.GetRelativePath(root, readme)}, the readme {package} packs, holds what nuget.org does not render: " +
                "it shows Markdown without HTML, and neither a relative link or image nor an image from a host it does not allow " +
                $"({_nugetOrgReadmeRules}).");
        }
    }

    [Fact]
    public void TheRepositoryReadme_IsRefusedAsAPackageReadme_AndThePackagesOwnIsNot()
    {
        string root = ProjectReferenceGraph.RepositoryRoot;

        NugetOrgRefusals(File.ReadAllText(Path.Combine(root, "README.md"))).ShouldNotBeEmpty(
            "The repository README, whose header is HTML and whose links are relative, passed the nuget.org rule, so the rule examines nothing.");
        NugetOrgRefusals(File.ReadAllText(Path.Combine(root, ".template.config", "package", "README.md"))).ShouldBeEmpty();
    }

    private static string PackedReadme(string root, string package)
    {
        string projectPath = Path.Combine(root, package);
        string projectFolder = Path.GetDirectoryName(projectPath)!;
        var project = XDocument.Load(projectPath);
        string readmeName = project.Descendants("PackageReadmeFile").Single().Value;

        var packed = project.Descendants("None")
            .Where(item => (string?)item.Attribute("Pack") == "true")
            .Select(item => item.Attribute("Include")!.Value
                .Replace("$(MSBuildThisFileDirectory)", string.Empty, StringComparison.Ordinal)
                .Replace("$(TemplateRoot)", Path.GetRelativePath(projectFolder, root) + "/", StringComparison.Ordinal))
            .Where(include => Path.GetFileName(include) == readmeName)
            .ToList();

        packed.Count.ShouldBe(1, $"{package} names {readmeName} as its readme but packs {packed.Count} file(s) by that name.");

        return Path.GetFullPath(Path.Combine(projectFolder, packed[0]));
    }

    private static List<string> NugetOrgRefusals(string markdown)
    {
        string prose = _codeSpan.Replace(_fencedBlock.Replace(markdown, string.Empty), string.Empty);
        var refusals = new List<string>();

        refusals.AddRange(_htmlTag.Matches(prose).Select(tag => $"HTML: {tag.Value}"));

        refusals.AddRange(_linkTarget.Matches(prose)
            .Select(target => target.Groups["url"].Value)
            .Where(url => !IsAbsolute(url))
            .Select(url => $"relative link or image: {url}"));
        refusals.AddRange(_imageTarget.Matches(prose)
            .Select(target => target.Groups["url"].Value)
            .Where(url => IsAbsolute(url) && !IsAllowedImage(new Uri(url)))
            .Select(url => $"image from a host nuget.org does not allow: {url}"));

        return refusals;
    }

    private static bool IsAbsolute(string url) =>
        url.StartsWith("https://", StringComparison.Ordinal) || url.StartsWith("http://", StringComparison.Ordinal);

    private static bool IsAllowedImage(Uri image) =>
        image.Host == "github.com"
            ? _workflowBadge.IsMatch(image.AbsolutePath)
            : _nugetOrgImageHosts.Contains(image.Host, StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> RepositoryTextFiles(string root)
    {
        string[] skipped = [".git", ".claude", ".vs", ".idea", ".sonarqube", "bin", "obj", "node_modules", "TestResults", "artifacts", "plans"];
        string[] textExtensions = [".md", ".cs", ".csproj", ".props", ".targets", ".json", ".yml", ".yaml", ".xml", ".txt", ".http", ".sh", ".ps1", ".ts", ".js", ".html"];

        return new DirectoryInfo(root).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
            .Where(file => !Path.GetRelativePath(root, file.FullName).Split(Path.DirectorySeparatorChar).SkipLast(1).Any(skipped.Contains))
            .Where(file => textExtensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase) || file.Name is ".env.example" or "Dockerfile")
            .Where(file => Path.GetRelativePath(root, file.FullName) != "CHANGELOG.md")
            .Select(file => file.FullName);
    }

    [Fact]
    public void NoShippedFile_StartsWithTwoByteOrderMarks()
    {
        string root = ProjectReferenceGraph.RepositoryRoot;
        string[] skipped = [".git", ".claude", "bin", "obj", "node_modules", "TestResults"];

        IEnumerable<string> shipped = new DirectoryInfo(root).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
            .Where(file => !Path.GetRelativePath(root, file.FullName).Split(Path.DirectorySeparatorChar).SkipLast(1).Any(skipped.Contains))
            .Select(file => file.FullName);

        DoubledByteOrderMarks(shipped).Select(file => Path.GetRelativePath(root, file)).ShouldBeEmpty(
            "starts with two UTF-8 byte order marks, which the template engine copies as written: keep one.");
    }

    [Fact]
    public void ADoubledByteOrderMark_IsFound_AndASingleOneIsNot()
    {
        string folder = Directory.CreateTempSubdirectory("doubled-bom-").FullName;
        try
        {
            string doubled = Path.Combine(folder, "doubled.md");
            string single = Path.Combine(folder, "single.md");
            File.WriteAllBytes(doubled, [.. _utf8Bom, .. _utf8Bom, (byte)'#']);
            File.WriteAllBytes(single, [.. _utf8Bom, (byte)'#']);

            DoubledByteOrderMarks([doubled, single]).ShouldBe([doubled]);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static IEnumerable<string> DoubledByteOrderMarks(IEnumerable<string> files)
    {
        byte[] start = new byte[_utf8Bom.Length * 2];
        foreach (string file in files)
        {
            using FileStream stream = File.OpenRead(file);
            if (stream.Read(start) == start.Length && start.Take(_utf8Bom.Length).SequenceEqual(_utf8Bom) && start.Skip(_utf8Bom.Length).SequenceEqual(_utf8Bom))
            {
                yield return file;
            }
        }
    }

    [Fact]
    public void EveryReleasePleaseFile_IsSavedWithoutABom()
    {
        string root = ProjectReferenceGraph.RepositoryRoot;
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, ".github", "release-please", "config.json")));

        string[] extraFiles = [.. config.RootElement.GetProperty("packages").EnumerateObject()
            .Where(package => package.Value.TryGetProperty("extra-files", out _))
            .SelectMany(package => package.Value.GetProperty("extra-files").EnumerateArray())
            .Select(file => file.GetProperty("path").GetString()!)];

        extraFiles.ShouldNotBeEmpty("release-please's config.json lists no extra-files, so this rule is reading the wrong file.");

        string[] files = [.. extraFiles, ".github/release-please/config.json", ".github/release-please/manifest.json"];

        foreach (string file in files)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(root, file));

            bytes.Take(_utf8Bom.Length).SequenceEqual(_utf8Bom).ShouldBeFalse(
                $"'{file}' starts with a UTF-8 byte order mark, which release-please cannot parse: save it as UTF-8 without a BOM.");
        }
    }

    [Theory]
    [InlineData(".template.config", "(!refusedOptions && api)", "dotnet tool restore, then dotnet cleanarch bootstrap")]
    [InlineData(".template.config", "(!refusedOptions && api)", "the network, not Docker")]
    [InlineData(".template.config", "(!refusedOptions && api)", "writes the OpenAPI reference")]
    [InlineData(".template.config", "(!refusedOptions && api)", "git init")]
    [InlineData(".template.config", "(!refusedOptions && api)", "new --setup")]
    [InlineData(".template.config", "(!refusedOptions && !api)", "git init")]
    [InlineData(".template.config", "(!refusedOptions && !api)", "new --setup")]
    [InlineData(".template.config", "(!refusedOptions && !api)", "dotnet tool restore, then dotnet cleanarch bootstrap")]
    [InlineData(".template.config", "(!refusedOptions && !api)", "the network, not Docker")]
    [InlineData(".template.config", "(!refusedOptions && scaffoldedAtBootstrap)", "scaffold.json")]
    [InlineData(".template.config", "(!refusedOptions && dotnetClient)", "the client in src/Presentation")]
    [InlineData(".template.config", "(!refusedOptions && anyWeb)", "Node.js and pnpm")]
    [InlineData(".template.config/webapp/.template.config", null, "dotnet cleanarch add web-app --name <name> --framework <framework>")]
    [InlineData(".template.config/webpackage/.template.config", null, "dotnet cleanarch add web-package --name <name>")]
    [InlineData(".template.config/webapp/.template.config", "(webFramework)", "dotnet cleanarch add web-app --name <name>")]
    [InlineData(".template.config/webapp/.template.config", "(webFramework)", "Node.js, pnpm")]
    [InlineData(".template.config/webapp/.template.config", "(webPlaceholder)", "pnpm install")]
    [InlineData(".template.config/presentation/.template.config", null, "dotnet cleanarch add presentation --name <name>")]
    [InlineData(".template.config/presentation/.template.config", null, "creates the project as its scaffold.json describes")]
    public void Generation_EndsWithWhatToRunNext_ForTheShapeGenerated(string folder, string? condition, string named)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectReferenceGraph.RepositoryRoot, folder, "template.json")));
        string[] instructions = manifest.RootElement.TryGetProperty("postActions", out JsonElement actions)
            ? [.. actions.EnumerateArray()
                .Where(action => action.GetProperty("actionId").GetString() == _manualInstructions && Text(action, "condition") == condition)
                .SelectMany(action => action.GetProperty("manualInstructions").EnumerateArray())
                .Select(instruction => instruction.GetProperty("text").GetString()!)]
            : [];

        instructions.ShouldContain(
            text => text.Contains(named, StringComparison.Ordinal),
            $"{folder}/template.json prints no instruction naming '{named}' when {condition ?? "always"}, so dotnet new ends with "
            + "\"created successfully\" and nothing says what must run next, nor what it needs.");
    }

    [Fact]
    public void TheProjectsInstructions_AreLeftOutOfARefusedGeneration()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath()));

        manifest.RootElement.GetProperty("postActions").EnumerateArray()
            .Select(action => Text(action, "condition") ?? "(always)")
            .ShouldAllBe(
                condition => condition.StartsWith("(!refusedOptions", StringComparison.Ordinal),
                "A refused generation writes only RefusedGenerationOptions.cs, so an instruction to bootstrap it would be false.");
    }

    [Fact]
    public void EveryStarterHoldingAScaffold_IsOneTheBootstrapInstructionCovers()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        JsonElement symbols = manifest.RootElement.GetProperty("symbols");
        HashSet<string> covered = Reached(symbols, ["scaffoldedAtBootstrap"]);
        string[] everyValueOf = [.. covered
            .Select(name => Text(symbols.GetProperty(name), "value") ?? string.Empty)
            .Select(value => _everyValueBut.Match(value))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)];

        string[] uncovered = [.. manifest.RootElement.GetProperty("sources").EnumerateArray()
            .Where(source => (Text(source, "source") ?? string.Empty).StartsWith("./.template.config/starters/", StringComparison.Ordinal))
            .Where(source => Directory.EnumerateFiles(
                Path.Combine(ProjectReferenceGraph.RepositoryRoot, Text(source, "source")!), _scaffoldDescriptor, SearchOption.AllDirectories).Any())
            .Select(source => Text(source, "condition")!)
            .Where(condition => !_identifier.Matches(_quoted.Replace(condition, string.Empty)).Any(name =>
                covered.Contains(name.Value)
                || Reached(symbols, [name.Value]).Where(reached => Text(symbols.GetProperty(reached), "type") == "parameter").ToArray()
                    is { Length: > 0 } parameters && parameters.All(everyValueOf.Contains)))];

        uncovered.ShouldBeEmpty(
            "These starters ship a scaffold.json bootstrap creates, under a condition scaffoldedAtBootstrap does not reach: "
            + "add their symbol to it, so dotnet new says bootstrap creates them.");
    }

    private const string _manualInstructions = "AC1156F7-BB77-4DB8-B28F-24EEBCCA1E5C";

    private const string _scaffoldDescriptor = "scaffold.json";

    private static readonly Regex _everyValueBut = new(@"^\((\w+) != ""none""\)$", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static HashSet<string> Reached(JsonElement symbols, IEnumerable<string> from)
    {
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(from);
        while (pending.TryPop(out string? name))
        {
            if (!symbols.TryGetProperty(name, out JsonElement symbol) || !reached.Add(name))
            {
                continue;
            }

            if (Text(symbol, "type") == "computed")
            {
                foreach (Match used in _identifier.Matches(_quoted.Replace(Text(symbol, "value") ?? string.Empty, string.Empty)))
                {
                    pending.Push(used.Value);
                }
            }
        }

        return reached;
    }

    [Theory]
    [InlineData(".template.config")]
    [InlineData(".template.config/webapp/.template.config")]
    [InlineData(".template.config/webpackage/.template.config")]
    [InlineData(".template.config/presentation/.template.config")]
    public void TheIdeHost_ShowsEveryOptionButTheRefusals(string folder)
    {
        string directory = Path.Combine(ProjectReferenceGraph.RepositoryRoot, folder);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "template.json")));
        using var host = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "ide.host.json")));

        var parameters = manifest.RootElement.GetProperty("symbols").EnumerateObject()
            .Where(symbol => symbol.Value.GetProperty("type").GetString() == "parameter")
            .ToList();
        var options = parameters.Where(symbol => !IsRefusal(symbol.Value)).Select(symbol => symbol.Name).Order(StringComparer.Ordinal).ToList();
        var shown = host.RootElement.GetProperty("symbolInfo").EnumerateArray()
            .Where(symbol => symbol.GetProperty("isVisible").GetBoolean())
            .Select(symbol => symbol.GetProperty("id").GetString()!)
            .Order(StringComparer.Ordinal)
            .ToList();

        host.RootElement.GetProperty("defaultSymbolVisibility").GetBoolean().ShouldBeFalse(
            $"{folder}/ide.host.json shows every symbol by default, the refusals among them.");
        shown.ShouldBe(options, $"{folder}/ide.host.json must show each option of its template.json and no refusal.");
        File.Exists(Path.Combine(directory, host.RootElement.GetProperty("icon").GetString()!)).ShouldBeTrue(
            $"{folder}/ide.host.json names an icon that is not beside it.");
    }

    [Theory]
    [InlineData("sqlserver", "sqlServer")]
    [InlineData("sqlite", "sqlite")]
    public void ADatabase_IsRefusedExactlyUntilItsOverlayIsWired(string choice, string symbol)
    {
        string overlay = $".template.config/variants/database-{choice}/";
        bool overlaid = File.Exists(Path.Combine(
            ProjectReferenceGraph.RepositoryRoot,
            overlay,
            "src/Infrastructure/AT.Infrastructure.Core/Common/Contexts/DatabaseProvider.cs"));
        using var manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        var symbols = manifest.RootElement.GetProperty("symbols");
        string condition = $"(database == \"{choice}\")";

        bool refused = symbols.EnumerateObject().Any(candidate =>
            IsRefusal(candidate.Value)
            && candidate.Value.TryGetProperty("isRequired", out var required)
            && required.GetString() == condition);
        bool inRefusedOptions = _combination.Replace(symbols.GetProperty("refusedOptions").GetProperty("value").GetString()!, string.Empty)
            .Contains($"database == \"{choice}\"", StringComparison.Ordinal);
        bool sourced = manifest.RootElement.GetProperty("sources").EnumerateArray().Any(source =>
            source.TryGetProperty("source", out var path) && path.GetString() == "./" + overlay
            && source.TryGetProperty("condition", out var when) && when.GetString() == $"({symbol})");

        if (overlaid)
        {
            refused.ShouldBeFalse($"{overlay} holds the {choice} overlay, so template.json must stop refusing --database {choice}: remove its refusal symbol and its dotnetcli.host.json entry.");
            inRefusedOptions.ShouldBeFalse($"{overlay} holds the {choice} overlay, so refusedOptions must stop naming --database {choice}.");
            sourced.ShouldBeTrue($"{overlay} holds the {choice} overlay, but no source of template.json copies it under the condition ({symbol}).");
        }
        else
        {
            refused.ShouldBeTrue($"No overlay for {choice} exists under {overlay}, so --database {choice} must be refused by a symbol required when {condition}.");
            inRefusedOptions.ShouldBeTrue($"No overlay for {choice} exists, so refusedOptions must name --database {choice}.");
            sourced.ShouldBeFalse($"template.json copies {overlay}, which holds no overlay.");
        }
    }

    [Theory]
    [InlineData(".template.config", "web", "./.template.config/starters/")]
    [InlineData(".template.config/webapp/.template.config", "framework", "../starters/")]
    public void EveryFrameworkChoice_IsAStarterTheTemplateCopiesByIt(string folder, string option, string startersSource)
    {
        string starters = StartersFolder;
        string[] folders = WebStarters();
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectReferenceGraph.RepositoryRoot, folder, "template.json")));
        var symbols = manifest.RootElement.GetProperty("symbols");
        string[] frameworks = [.. symbols.GetProperty(option).GetProperty("choices").EnumerateArray()
            .Select(choice => choice.GetProperty("choice").GetString()!)
            .Where(choice => choice is not "none" and not "placeholder")
            .Order(StringComparer.Ordinal)];
        var computed = symbols.EnumerateObject()
            .Where(symbol => symbol.Value.GetProperty("type").GetString() == "computed")
            .Where(symbol => symbol.Name != "refusedOptions")
            .ToDictionary(symbol => symbol.Name, symbol => symbol.Value.GetProperty("value").GetString()!);
        var sources = manifest.RootElement.GetProperty("sources").EnumerateArray().ToList();

        folders.ShouldBeSubsetOf(frameworks, $"Each folder under .template.config/starters is named after the --{option} value it creates.");

        foreach (string framework in frameworks)
        {
            string[] chosenBy = [.. computed.Where(symbol => ChosenValues(symbol.Value, option).Contains(framework)).Select(symbol => symbol.Key)];
            chosenBy.Length.ShouldBe(1, $"Exactly one computed symbol of {folder}/template.json must hold ({option} == \"{framework}\"), the one that copies its starter.");
            string symbol = chosenBy[0];
            string[] copied = [.. sources
                .Where(source => Text(source, "target") == "./src/Web/apps/"
                    && Text(source, "condition") == $"({symbol})"
                    && Text(source, "source") is { } path && path.StartsWith(startersSource, StringComparison.Ordinal))
                .Select(source => Text(source, "source")![startersSource.Length..].TrimEnd('/'))];
            copied.Length.ShouldBe(1, $"{folder}/template.json must copy one folder of {startersSource} into ./src/Web/apps/ under the condition ({symbol}), so --{option} {framework} gets its starter.");
            string starter = copied[0];

            folders.ShouldContain(starter, $"{folder}/template.json copies {startersSource}{starter}/, which does not exist.");
            File.Exists(Path.Combine(starters, starter, "FrameworkSite", "scaffold.json")).ShouldBeTrue(
                $".template.config/starters/{starter}/FrameworkSite holds no scaffold.json, so web-scaffold would find nothing to run.");
            starter.ShouldBe(framework, $"--{option} {framework} must copy its own starter, {startersSource}{framework}/: a framework has one value, never a second name for the same app.");
            Reaches(computed, "webFramework", symbol).ShouldBeTrue($"webFramework in {folder}/template.json must reach {symbol}, so the shared wiring comes with the {framework} starter.");
        }
    }

    [Theory]
    [InlineData(".template.config", "web", "webRendering", "./.template.config/starters/")]
    [InlineData(".template.config/webapp/.template.config", "framework", "rendering", "../starters/")]
    public void ServerRendering_IsCopiedFromTheStartersHoldingIt_AndRefusedForEveryOther(string folder, string option, string rendering, string startersSource)
    {
        string[] serverStarters = [.. WebStarters().Where(starter => File.Exists(Path.Combine(StartersFolder, starter, "ssr", "FrameworkSite", "scaffold.json")))];
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectReferenceGraph.RepositoryRoot, folder, "template.json")));
        var symbols = manifest.RootElement.GetProperty("symbols");
        var computed = symbols.EnumerateObject()
            .Where(symbol => symbol.Value.GetProperty("type").GetString() == "computed")
            .Where(symbol => symbol.Name != "refusedOptions")
            .ToDictionary(symbol => symbol.Name, symbol => symbol.Value.GetProperty("value").GetString()!);
        var sources = manifest.RootElement.GetProperty("sources").EnumerateArray().ToList();
        string refusal = $"({rendering} == \"ssr\" && {string.Join(" && ", serverStarters.Select(starter => $"{option} != \"{starter}\""))})";

        serverStarters.ShouldNotBeEmpty("No starter under .template.config/starters holds ssr/FrameworkSite/scaffold.json, so this rule reads nothing.");
        ChoiceValues(symbols.GetProperty(rendering)).ShouldBe(["spa", "ssr"], $"--{rendering} offers spa and ssr in {folder}/template.json.");
        Facet(symbols.GetProperty(rendering), "defaultValue").ShouldBe("spa", $"--{rendering} keeps the single-page app as its default.");
        computed["webServerRendering"].ShouldContain($"{rendering} == \"ssr\"", Case.Sensitive, $"webServerRendering in {folder}/template.json must hold when --{rendering} is ssr.");
        Facet(symbols.GetProperty("serverRenderingNeedsAServerStarter"), "isRequired").ShouldBe(
            refusal,
            $"{folder}/template.json must refuse --{rendering} ssr for exactly the starters without an ssr folder, which the starters holding one are not.");

        foreach (string starter in serverStarters)
        {
            string symbol = computed.Single(pair => ChosenValues(pair.Value, option).Contains(starter)).Key;
            var spa = sources.Single(source => Text(source, "source") == $"{startersSource}{starter}/");
            string[] excluded = [.. spa.GetProperty("modifiers").EnumerateArray()
                .Select(modifier => $"{Text(modifier, "condition")}:{string.Join(",", modifier.GetProperty("exclude").EnumerateArray().Select(path => path.GetString()))}")];

            excluded.ShouldBe([":ssr/**", "(webServerRendering):FrameworkSite/**"], $"{folder}/template.json must copy {startersSource}{starter}/ without its ssr folder, and without its FrameworkSite under webServerRendering, since the ssr folder holds the app's FrameworkSite then.");
            sources.Count(source => Text(source, "source") == $"{startersSource}{starter}/ssr/"
                    && Text(source, "target") == "./src/Web/apps/"
                    && Text(source, "condition") == $"({symbol} && webServerRendering)")
                .ShouldBe(1, $"{folder}/template.json must copy {startersSource}{starter}/ssr/ into ./src/Web/apps/ under ({symbol} && webServerRendering).");
        }
    }

    [Theory]
    [InlineData(".template.config", "presentation", "./.template.config/starters/", new string[0])]
    [InlineData(".template.config/presentation/.template.config", "framework", "../starters/", new string[0])]
    public void EveryPresentationChoice_IsAStarterTheTemplateCopiesByIt(string folder, string option, string startersSource, string[] notOffered)
    {
        string[] folders = [.. PresentationStarters().Except(notOffered, StringComparer.Ordinal)];
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectReferenceGraph.RepositoryRoot, folder, "template.json")));
        var symbols = manifest.RootElement.GetProperty("symbols");
        string[] presentations = [.. symbols.GetProperty(option).GetProperty("choices").EnumerateArray()
            .Select(choice => choice.GetProperty("choice").GetString()!)
            .Where(choice => choice is not _apiChoice)
            .Order(StringComparer.Ordinal)];
        var computed = symbols.EnumerateObject()
            .Where(symbol => symbol.Value.GetProperty("type").GetString() == "computed")
            .Where(symbol => symbol.Name != "refusedOptions")
            .ToDictionary(symbol => symbol.Name, symbol => symbol.Value.GetProperty("value").GetString()!);
        var sources = manifest.RootElement.GetProperty("sources").EnumerateArray().ToList();

        folders.ShouldBe(presentations, $"Each --{option} value of {folder}/template.json but none is a folder under .template.config/starters holding the project's scaffold.json, and each such folder is a value.");

        foreach (string presentation in presentations)
        {
            string[] chosenBy = [.. computed.Where(symbol => ChosenValues(symbol.Value, option).Contains(presentation)).Select(symbol => symbol.Key)];
            chosenBy.Length.ShouldBe(1, $"Exactly one computed symbol of {folder}/template.json must hold ({option} == \"{presentation}\"), the one that copies its starter.");
            sources.Count(source => Text(source, "source") == $"{startersSource}{presentation}/"
                    && Text(source, "target") == "./"
                    && Text(source, "condition") == $"({chosenBy[0]})")
                .ShouldBe(1, $"{folder}/template.json must copy {startersSource}{presentation}/ into ./ under the condition ({chosenBy[0]}).");

            using var descriptor = JsonDocument.Parse(File.ReadAllText(PresentationDescriptor(presentation)));
            switch (Text(descriptor.RootElement, "kind"))
            {
                case "dotnet":
                    Text(descriptor.RootElement, "template").ShouldNotBeNullOrWhiteSpace($"The {presentation} starter runs a dotnet new template, which its scaffold.json must name.");
                    break;
                case "written":
                    Text(descriptor.RootElement, "generator").ShouldNotBeNullOrWhiteSpace($"The {presentation} starter is written whole, and its scaffold.json names the generator whose shape it follows.");
                    Text(descriptor.RootElement, "version").ShouldNotBeNullOrWhiteSpace($"The {presentation} starter is written whole, and its scaffold.json names the version of the generator it follows.");
                    descriptor.RootElement.TryGetProperty("template", out _).ShouldBeFalse($"The {presentation} starter is written whole, and its scaffold.json names no template to run.");
                    File.Exists(Path.Combine(StarterProject(presentation), Text(descriptor.RootElement, "name") + ".csproj"))
                        .ShouldBeTrue($"The {presentation} starter is written whole, and holds its project file.");
                    break;
                default:
                    Assert.Fail($"The {presentation} starter's scaffold.json says neither kind dotnet, for a template it runs, nor written, for a project it holds whole.");
                    break;
            }
        }
    }

    [Fact]
    public void EveryStarterFolder_IsAWebOrAPresentationStarter()
    {
        string[] all = [.. Directory.GetDirectories(StartersFolder).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];

        all.ShouldBe(
            [.. WebStarters().Concat(PresentationStarters()).Order(StringComparer.Ordinal)],
            "A folder under .template.config/starters holds FrameworkSite/scaffold.json for --web, or the presentation project's scaffold.json for --presentation; one holding none is copied by no option.");
    }

    [Fact]
    public void TheWorkerStarter_IsWrittenWhole_AndCopiedByJobsWorker()
    {
        string project = Path.Combine(StartersFolder, _workerStarter, "src", "Presentation", ProjectFolders.FolderOf("AppTemplate.Worker"));
        using var descriptor = JsonDocument.Parse(File.ReadAllText(Path.Combine(project, "scaffold.json")));
        using var manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath()));

        Text(descriptor.RootElement, "kind").ShouldBe("written", "The worker's starter holds its whole project, which its scaffold.json must say, so presentation-scaffold runs no generator.");
        Text(descriptor.RootElement, "generator").ShouldNotBeNullOrWhiteSpace("A written starter names the generator whose shape it follows.");
        Text(descriptor.RootElement, "version").ShouldNotBeNullOrWhiteSpace("A written starter names the version of the generator it follows.");
        descriptor.RootElement.TryGetProperty("template", out _).ShouldBeFalse("A written starter names no template to run.");
        File.Exists(Path.Combine(project, "AppTemplate.Worker.csproj")).ShouldBeTrue("A written starter holds its project file.");
        File.Exists(Path.Combine(project, "Program.cs")).ShouldBeTrue("A written starter holds its composition root.");
        manifest.RootElement.GetProperty("sources").EnumerateArray()
            .Count(source => Text(source, "source") == $"./.template.config/starters/{_workerStarter}/"
                && Text(source, "target") == "./"
                && Text(source, "condition") == "(jobsWorker)")
            .ShouldBe(1, "template.json must copy the worker's starter into ./ under the condition (jobsWorker).");
    }

    [Fact]
    public void TheFoldersTheWorkerTakes_AreTheOnesTheApiLeavesOut()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        var sources = manifest.RootElement.GetProperty("sources").EnumerateArray().ToList();

        string[] taken = [.. MovedSources(sources)
            .SelectMany(source => source.GetProperty("include").EnumerateArray()
                .Select(include => Text(source, "source")![2..] + include.GetString()))
            .Order(StringComparer.Ordinal)];
        string[] leftOut = [.. sources[0].GetProperty("modifiers").EnumerateArray()
            .Where(modifier => Text(modifier, "condition") == "(jobsWorker)")
            .SelectMany(modifier => modifier.GetProperty("exclude").EnumerateArray().Select(path => path.GetString()!))
            .Order(StringComparer.Ordinal)];

        taken.Length.ShouldBeGreaterThanOrEqualTo(3, "Far fewer moved folders were found than the worker takes, so this rule is not reading template.json.");
        leftOut.ShouldBe(taken, "Under jobsWorker the API's source must leave out exactly the folders the worker's sources copy, or a job would run in both hosts or in neither.");
    }

    [Fact]
    public void EveryNamespaceTheWorkerTakes_HasItsSwitchSymbol()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        var symbols = manifest.RootElement.GetProperty("symbols");
        var switched = symbols.EnumerateObject()
            .Where(symbol => symbol.Name.StartsWith("jobsNamespace", StringComparison.Ordinal))
            .ToDictionary(
                symbol => Text(symbol.Value, "replaces")!,
                symbol => symbol.Value.GetProperty("parameters").GetProperty("cases").EnumerateArray()
                    .Single(@case => Text(@case, "condition") == "(jobsWorker)").GetProperty("value").GetString()!,
                StringComparer.Ordinal);

        string[] declared = [.. MovedSources(manifest.RootElement.GetProperty("sources").EnumerateArray().ToList())
            .SelectMany(MovedFiles)
            .SelectMany(File.ReadLines)
            .Select(line => _declaredNamespace.Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

        declared.Length.ShouldBeGreaterThanOrEqualTo(5, "Far fewer namespaces were found in the moved folders than they declare, so this rule is not reading them.");
        declared.Where(space => !switched.ContainsKey(space)).ShouldBeEmpty(
            "A namespace declared in a folder --presentation worker moves needs a generated switch symbol of template.json that replaces it, or the moved file keeps the API's namespace in the worker.");
        declared.Where(space => switched.TryGetValue(space, out string? worker) && worker != "Worker." + space["Api.".Length..]).ShouldBeEmpty(
            "Each switch symbol of a moved namespace gives the worker the same namespace with Worker in place of Api.");
    }

    [Fact]
    public void ThePresentationItemsNameRefusal_NamesEveryProjectTheTemplateWrites()
    {
        const string prefix = "AppTemplate.";

        string[] projects = [.. _projectRoots
            .SelectMany(root => Directory.EnumerateFiles(Path.Combine(ProjectReferenceGraph.RepositoryRoot, root), "*.csproj", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
            .Select(name => name[prefix.Length..])
            .Order(StringComparer.Ordinal)];

        projects.Length.ShouldBeGreaterThanOrEqualTo(10, "Far fewer projects were found than this template writes, so the walk is not reading its tree.");

        using var item = JsonDocument.Parse(File.ReadAllText(PresentationItemManifestPath()));
        string itemRefusal = item.RootElement.GetProperty("symbols").GetProperty("presentationNameIsTaken").GetProperty("isRequired").GetString()!;
        projects
            .Where(project => !itemRefusal.Contains($"name == \"{project}\"", StringComparison.Ordinal))
            .ShouldBeEmpty("cleanarch-presentation must refuse each -n that names one of the template's projects or its test project, which the item would overwrite: add the name to presentationNameIsTaken of .template.config/presentation/.template.config/template.json.");
    }

    [Theory]
    [InlineData(".template.config", "nameNeedsTwoCharacters", "nameNeedsALeadingLetter", "name")]
    [InlineData(".template.config/presentation/.template.config", "projectNameNeedsTwoCharacters", "projectNameNeedsALeadingLetter", "projectName")]
    [InlineData(".template.config/webapp/.template.config", "projectNameNeedsTwoCharacters", "projectNameNeedsALeadingLetter", "projectName")]
    public void TheNameRefusals_RefuseEveryNameWhoseFoldersTheSolutionCannotHoldForItsProjects(string folder, string refusal, string letterRefusal, string nameSymbol)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectReferenceGraph.RepositoryRoot, folder, "template.json")));
        JsonElement symbols = manifest.RootElement.GetProperty("symbols");
        string condition = Text(symbols.GetProperty(refusal), "isRequired")!;
        string[] refused = [.. Regex.Matches(condition, $@"\b{nameSymbol} == ""([^""]*)""", RegexOptions.None, TimeSpan.FromSeconds(5)).Select(match => match.Groups[1].Value)];
        string[] oneCharacter = [.. _nameStarts.Select(start => start.ToString())];
        string[] collide = [.. Names(3).Where(FoldersDifferFromTheProjectsInCaseAlone)];

        collide.ShouldNotBeEmpty("No name was found whose folders differ from its projects in case alone, so this rule compares nothing.");
        condition.ShouldBe(
            $"({string.Join(" || ", refused.Select(name => $"{nameSymbol} == \"{name}\""))})",
            $"{folder}/template.json must read {refusal} as one {nameSymbol} == \"...\" per refused name, which this rule and the launcher evaluate.");
        oneCharacter.Where(name => !refused.Contains(name, StringComparer.OrdinalIgnoreCase)).ShouldBeEmpty(
            $"{folder}/template.json refuses a {nameSymbol} under two characters, the minimum its description names, through {refusal}: add each name here to it.");
        refused.Where(name => !oneCharacter.Contains(name, StringComparer.OrdinalIgnoreCase)).ShouldBeEmpty(
            $"{folder}/template.json refuses, through {refusal}, a {nameSymbol} of two characters or more, which generates a sound project.");
        collide.Where(name => !refused.Contains(name, StringComparer.OrdinalIgnoreCase)).ShouldBeEmpty(
            $"These names put each project in a folder differing from its name in case alone, which a solution takes for the same name: {folder}/template.json must refuse them through {refusal}.");

        string letterCondition = Text(symbols.GetProperty(letterRefusal), "isRequired")!;
        string guard = nameSymbol == "projectName" ? $@"{nameSymbol} != """" && " : string.Empty;
        Match bounds = Regex.Match(
            letterCondition,
            $@"^\({Regex.Escape(guard)}\(?{nameSymbol} < ""(?<below>[^""]+)"" \|\| {nameSymbol} >= ""(?<from>[^""]+)""\)?\)$",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));
        bounds.Success.ShouldBeTrue(
            $"{folder}/template.json must read {letterRefusal} as ({guard}{nameSymbol} < \"A\" || {nameSymbol} >= \"[\"), the one form of the rule the engine can evaluate on a parameter, which this rule and the launcher evaluate.");
        bool LetterRefuses(string name) =>
            !(guard.Length > 0 && name.Length == 0)
            && (string.Compare(name, bounds.Groups["below"].Value, StringComparison.OrdinalIgnoreCase) < 0
                || string.Compare(name, bounds.Groups["from"].Value, StringComparison.OrdinalIgnoreCase) >= 0);
        string[] anyNames = [.. AnyNames(3)];

        anyNames.Where(name => ProjectFolders.InitialsOf(name).Length == 0 && !LetterRefuses(name)).ShouldBeEmpty(
            $"These names have no letter from A to Z to take initials from, so their projects would sit in folders such as .Api: {folder}/template.json must refuse them through {letterRefusal}.");
        anyNames.Where(name => LetterRefuses(name) == char.IsAsciiLetter(name[0])).ShouldBeEmpty(
            $"{folder}/template.json must refuse, through {letterRefusal}, exactly the {nameSymbol} that does not start with a letter from A to Z, the rule its description names.");

        if (nameSymbol == "name")
        {
            Text(symbols.GetProperty("refusedOptions"), "value")!.ShouldContain(
                condition,
                Case.Sensitive,
                $"refusedOptions must hold {refusal}'s condition, so a hand-passed {refusal} selects the refused variant.");
            Text(symbols.GetProperty(_nameLacksALeadingLetter), "value").ShouldBe(
                letterCondition,
                $"{_nameLacksALeadingLetter} must hold {letterRefusal}'s condition: the engine evaluates the ordering only in a symbol, never in a file's #if.");
            Text(symbols.GetProperty("refusedOptions"), "value")!.ShouldContain(
                $" || {_nameLacksALeadingLetter})",
                Case.Sensitive,
                $"refusedOptions must hold {_nameLacksALeadingLetter}, so a hand-passed {letterRefusal} selects the refused variant.");
            string refusedVariant = File.ReadAllText(Path.Combine(ProjectReferenceGraph.RepositoryRoot, ".template.config", "variants", "refused", "src", "Presentation", ProjectFolders.FolderOf("AppTemplate.Presentation.Core"), "RefusedGenerationOptions.cs"));
            refusedVariant.ShouldContain($"//#if {condition}", Case.Sensitive, $"RefusedGenerationOptions.cs must stop the build under {refusal}'s condition.");
            refusedVariant.ShouldContain($"//#if ({_nameLacksALeadingLetter})", Case.Sensitive, $"RefusedGenerationOptions.cs must stop the build under {letterRefusal}'s condition.");
        }
        else
        {
            LetterRefuses(string.Empty).ShouldBeFalse($"{folder}/template.json leaves {nameSymbol} empty where the item needs no project, which {letterRefusal} must let through.");
        }
    }

    private static IEnumerable<string> AnyNames(int longest)
    {
        IEnumerable<string> ofLength = [string.Empty];

        for (int length = 1; length <= longest; length++)
        {
            ofLength = [.. ofLength.SelectMany(name => _anyNameCharacters.Select(next => name + next))];

            foreach (string name in ofLength)
            {
                yield return name;
            }
        }
    }

    private static bool FoldersDifferFromTheProjectsInCaseAlone(string name) =>
        ProjectFolders.InitialsOf(name) is var initials
        && string.Equals(initials, name, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(initials, name, StringComparison.Ordinal);

    private static List<string> Names(int longest)
    {
        IEnumerable<string> ofLength = [string.Empty];
        var names = new List<string>();

        for (int length = 1; length <= longest; length++)
        {
            ofLength = [.. ofLength.SelectMany(name => _nameCharacters.Select(next => name + next))];
            names.AddRange(ofLength.Where(name => name.Split('.').All(part => part.Length > 0 && _nameStarts.Contains(part[0]))));
        }

        return names;
    }

    [Fact]
    public void TheWpfStartersSharedCode_IsTheAvaloniaStartersOwn_ByteForByte()
    {
        string[] avalonia = SharedDesktopCode("avalonia");
        string[] wpf = SharedDesktopCode("wpf");

        avalonia.Length.ShouldBeGreaterThanOrEqualTo(20, "Far fewer files were found than the Avalonia starter's shared code and its tests hold, so this rule is not reading them.");
        wpf.ShouldBe(
            avalonia,
            "The WPF starter holds a copy of the Avalonia starter's hosting, desktop session, settings and their tests, at the same paths. Change both copies together; a third desktop client is the time to move them into a project of their own.");

        foreach (string path in avalonia)
        {
            File.ReadAllBytes(Path.Combine(StartersFolder, "wpf", path)).ShouldBe(
                File.ReadAllBytes(Path.Combine(StartersFolder, "avalonia", path)),
                $"wpf/{path} differs from avalonia/{path}: change both copies together.");
        }
    }

    [Fact]
    public void EveryHostsCopyOfTheApisAdapters_ReadsAsTheApisOwn_ApartFromItsNamespace()
    {
        int compared = 0;

        foreach (string adapter in _apiAdapters)
        {
            string own = NamespaceNeutral(Path.Combine(ProjectReferenceGraph.RepositoryRoot, "src", "Presentation", "AT.Api", adapter));

            foreach ((string starter, string project) in _hostsOfTheApisAdapters)
            {
                string copy = Path.Combine(StartersFolder, starter, "src", "Presentation", project, adapter);

                if (!File.Exists(copy))
                {
                    continue;
                }

                compared++;
                NamespaceNeutral(copy).ShouldBe(
                    own,
                    $"{starter}/{project}/{adapter} is a copy of src/Presentation/AT.Api/{adapter}: change both together, whatever the namespace.");
            }
        }

        compared.ShouldBeGreaterThanOrEqualTo(16, "Far fewer copies of the API's adapters were found than the starters hold, so this rule is not reading them.");
    }

    private static readonly string[] _apiAdapters =
    [
        "Common/Security/BackgroundAuditActor.cs",
        "Common/Security/CurrentUserAuditActor.cs",
        "Features/Auth/Composition/AccountUserAccountDirectory.cs",
        "Features/ExternalIdentity/Composition/ExternalIdentityUserAccountDirectory.cs",
        "Features/ExternalIdentity/Composition/ExternalPrincipalMapper.cs",
        "Features/Reminders/Composition/AccountReminderRecipientDirectory.cs",
        "Features/Reminders/Composition/ExternalIdentityReminderRecipientDirectory.cs",
    ];

    private static readonly (string Starter, string Project)[] _hostsOfTheApisAdapters =
    [
        ("blazor-server", "AT.PresentationAppPortal"),
        ("mcp", "AT.PresentationAppMcp"),
        ("worker", "AT.Worker"),
    ];

    private static string NamespaceNeutral(string file) =>
        _fileScopedNamespace.Replace(File.ReadAllText(file), "namespace AppTemplate;", 1);

    private static readonly Regex _fileScopedNamespace = new(@"^namespace \S+;", RegexOptions.Multiline, TimeSpan.FromSeconds(5));

    [Theory]
    [InlineData(".template.config", "name")]
    [InlineData(".template.config/presentation/.template.config", "projectName")]
    [InlineData(".template.config/webapp/.template.config", "projectName")]
    public void TheFolderSymbol_GivesTheProductsInitials_AsTheToolsComputeThem(string folder, string nameSymbol)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(ProjectReferenceGraph.RepositoryRoot, folder, "template.json")));
        using var project = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        var symbols = manifest.RootElement.GetProperty("symbols");
        JsonElement anyCase = symbols.GetProperty(_folderPrefixAnyCaseSymbol);
        JsonElement prefix = symbols.GetProperty(_folderPrefixSymbol);
        string token = ProjectFolders.InitialsOf(ProjectFolders.ProductName) + ".";

        Text(anyCase, "generator").ShouldBe("regex", $"{folder}/template.json computes {_folderPrefixAnyCaseSymbol} with the regex generator.");
        Text(anyCase.GetProperty("parameters"), "source").ShouldBe(nameSymbol, $"{folder}/template.json computes the initials from {nameSymbol}, the product's name.");
        anyCase.GetProperty("parameters").GetProperty("steps").GetRawText().ShouldBe(
            project.RootElement.GetProperty("symbols").GetProperty(_folderPrefixAnyCaseSymbol).GetProperty("parameters").GetProperty("steps").GetRawText(),
            $"{folder}/template.json computes the initials with other steps than .template.config/template.json: a project's folder must read the same from every template.");
        Text(prefix, "generator").ShouldBe("casing", $"{folder}/template.json upper-cases the initials with the casing generator.");
        Text(prefix.GetProperty("parameters"), "source").ShouldBe(_folderPrefixAnyCaseSymbol);
        prefix.GetProperty("parameters").GetProperty("toLower").GetBoolean().ShouldBeFalse($"{folder}/template.json must upper-case the initials.");
        Text(prefix, "replaces").ShouldBe(token, $"{folder}/template.json must replace {token}, the folder prefix of this checkout's projects, in the files it writes.");
        Text(prefix, "fileRename").ShouldBe(token, $"{folder}/template.json must replace {token}, the folder prefix of this checkout's projects, in the paths it writes.");

        string[] steps = [.. anyCase.GetProperty("parameters").GetProperty("steps").EnumerateArray()
            .Select(step => $"{Text(step, "regex")}\n{Text(step, "replacement")}")];

        foreach (string name in _productNames)
        {
            string generated = steps
                .Select(step => step.Split('\n'))
                .Aggregate(name, (value, step) => Regex.Replace(value, step[0], step[1], RegexOptions.None, TimeSpan.FromSeconds(5)))
                .ToUpperInvariant();

            generated.ShouldBe(ProjectFolders.InitialsOf(name) + ".", $"{folder}/template.json names the folders of {name} {generated}, where the tools find them under {ProjectFolders.InitialsOf(name)}.");
        }
    }

    [Fact]
    public void EveryCopyOfTheInitials_ReadsAsTheArchitectureTestsOwn()
    {
        string[] copies =
        [
            Path.Combine("launcher", "CleanArch.Tooling", "ProjectNames.cs"),
        ];
        string own = SegmentInitials(Path.Combine("tests", "Architecture", ProjectFolders.FolderOf("AppTemplate.Architecture.Tests"), "Fixtures", "ProjectFolders.cs"));

        own.ShouldNotBeEmpty("ProjectFolders declares no SegmentInitials, so this rule compares nothing.");
        copies.Where(copy => SegmentInitials(copy) != own).ShouldBeEmpty(
            "The tools compute a project's folder on their own, and these compute the initials otherwise than ProjectFolders does: make each SegmentInitials read as that one.");
    }

    [Fact]
    public void TheFolderToken_NamesNothingButAProjectFolder()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        string token = Text(manifest.RootElement.GetProperty("symbols").GetProperty(_folderPrefixSymbol), "replaces")!;
        string root = ProjectReferenceGraph.RepositoryRoot;
        string[] suffixes = [.. _treesHoldingProjects
            .SelectMany(tree => Directory.EnumerateFiles(Path.Combine(root, tree), "AppTemplate.*.csproj", SearchOption.AllDirectories))
            .Select(path => Path.GetFileNameWithoutExtension(path)["AppTemplate.".Length..])
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(suffix => suffix.Length)];
        string[] generated = [.. ProjectFiles(root)
            .Concat(_generatedTemplateFolders.SelectMany(folder => Directory.EnumerateFiles(Path.Combine(root, folder), "*", SearchOption.AllDirectories)))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(relative => !relative.Split('/').SkipLast(1).Any(_foldersNoTemplateWrites.Contains))];

        suffixes.Length.ShouldBeGreaterThanOrEqualTo(20, "Far fewer projects were found than this template writes, so this rule is not reading its tree.");
        generated.Length.ShouldBeGreaterThanOrEqualTo(500, "Far fewer files were found than this template writes, so this rule is not reading its tree.");

        generated
            .SelectMany(relative => Collisions(relative, relative, token, suffixes)
                .Concat(File.ReadAllBytes(Path.Combine(root, relative)) is var bytes && !bytes.Contains((byte)0)
                    ? Collisions(relative, System.Text.Encoding.UTF8.GetString(bytes), token, suffixes)
                    : []))
            .ShouldBeEmpty(
                $"template.json replaces {token} with the product's initials in every path and file it writes, so {token} may stand only before a project's folder: "
                + "write these otherwise, or rename the token in the three template.json files and the tree.");
    }

    [Fact]
    public void TheLongestPathUnderSrcAndTests_StaysWithinItsBudget_UnderTheDefaultNameAppTemplate()
    {
        string root = ProjectReferenceGraph.RepositoryRoot;
        string[] paths = [.. _projectRoots
            .SelectMany(tree => ProjectFiles(Path.Combine(root, tree)))
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(relative => !relative.StartsWith("src/Web/", StringComparison.Ordinal))];

        paths.Length.ShouldBeGreaterThanOrEqualTo(500, "Far fewer files were found under src and tests than this template holds, so this rule is not reading its tree.");

        paths.Where(path => path.Length > _longestPathBudget).ShouldBeEmpty(
            $"Under the default name AppTemplate, no path under src and tests may exceed {_longestPathBudget} characters, "
            + "so that a project under a longer name in a deeper folder still checks out on Windows without core.longpaths: shorten each path named here.");
    }

    [Fact]
    public void TheLongestEntryUnderContent_StaysWithinItsBudget()
    {
        string root = ProjectReferenceGraph.RepositoryRoot;
        var excludes = PackageExcludes().Select(GlobPattern).ToList();
        var entries = PackedFiles(root, string.Empty)
            .Where(relative => !excludes.Any(exclude => exclude.IsMatch(relative)))
            .Select(relative => $"content/{relative}")
            .ToList();

        entries.Count.ShouldBeGreaterThanOrEqualTo(1000, "Far fewer files were found than the package carries, so this rule is not reading its tree.");

        entries.Where(entry => entry.Length > _longestEntryBudget).Select(entry => $"{entry.Length}: {entry}").ShouldBeEmpty(
            $"No entry the package places under content/ may exceed {_longestEntryBudget} characters, the longest one it carries today. "
            + $"NuGet reports NU5123 beyond {_nugetPathWarning}, and the entries between the two are accepted; shorten each entry named here.");
    }

    [Fact]
    public void ThePresentationItem_ReadsTheProjectsOptionsAsTheProjectTemplateDeclaresThem()
    {
        using var project = JsonDocument.Parse(File.ReadAllText(ManifestPath()));
        using var item = JsonDocument.Parse(File.ReadAllText(PresentationItemManifestPath()));
        var projectSymbols = project.RootElement.GetProperty("symbols");
        var itemSymbols = item.RootElement.GetProperty("symbols");

        string[] read = [.. PresentationStarters()
            .SelectMany(starter => Directory.EnumerateFiles(Path.Combine(StartersFolder, starter), "*", SearchOption.AllDirectories))
            .Append(Path.Combine(ProjectReferenceGraph.RepositoryRoot, "docs", "features", "presentation.md"))
            .SelectMany(File.ReadLines)
            .Select(line => _condition.Match(line))
            .Where(marker => marker.Success)
            .SelectMany(marker => _identifier.Matches(_quoted.Replace(marker.Groups[1].Value, string.Empty)).Select(name => name.Value))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

        read.Length.ShouldBeGreaterThanOrEqualTo(8, "Far fewer symbols were read from the presentation starters' markers than they use, so this rule is not reading them.");
        read.Where(symbol => !itemSymbols.TryGetProperty(symbol, out _)).ShouldBeEmpty(
            "The presentation starters or docs/features/presentation.md read these symbols, which cleanarch-presentation does not declare: declare each as the project template does.");

        foreach (var symbol in itemSymbols.EnumerateObject().Where(symbol => symbol.Name != "presentationNameIsTaken"))
        {
            string type = Text(symbol.Value, "type")!;
            if (!projectSymbols.TryGetProperty(symbol.Name, out var declared) || (type == "computed" && !read.Contains(symbol.Name)))
            {
                continue;
            }

            Text(declared, "type").ShouldBe(type, $"{symbol.Name} is a {type} symbol of cleanarch-presentation and of another type in the project template.");

            foreach (string facet in (string[])["datatype", "defaultValue", "isRequired", "value", "replaces"])
            {
                if (facet is "value" or "isRequired" && ChoosesTheStarterOrReadsTheProject(symbol.Value, facet))
                {
                    continue;
                }

                Facet(symbol.Value, facet).ShouldBe(Facet(declared, facet), $"cleanarch-presentation declares the {facet} of {symbol.Name} otherwise than the project template, so the starter it copies would not be the project's.");
            }

            ChoiceValues(symbol.Value).ShouldBe(ChoiceValues(declared), $"cleanarch-presentation offers other values of {symbol.Name} than the project template.");
        }

        foreach (string starter in PresentationStarters())
        {
            JsonNode.DeepEquals(StarterModifiers(project.RootElement, "./.template.config/starters/", starter), StarterModifiers(item.RootElement, "../starters/", starter)).ShouldBeTrue(
                $"cleanarch-presentation must leave out of the {starter} starter exactly what the project template leaves out, under the same conditions.");
        }
    }

    [Theory]
    [InlineData(
        "blazor-server/src/Presentation/AT.PresentationAppPortal/README.md",
        "anyPresentation|presentationBlazorServer",
        11,
        new[] { "docs/features/presentation.md" })]
    [InlineData(
        "avalonia/src/Presentation/AT.PresentationAppDesktop/README.md",
        "anyPresentation|presentationAvalonia|dotnetClient",
        9,
        new[] { "docs/features/presentation.md", "src/Presentation/AT.Presentation.Client/", "tests/Presentation/AT.Presentation.Client.UnitTests/", "AppTemplate.slnx" })]
    [InlineData(
        "mcp/src/Presentation/AT.PresentationAppMcp/README.md",
        "anyPresentation|presentationMcp|accessKeys",
        11,
        new[] { "docs/features/presentation.md", "docs/decisions/0005-an-mcp-server-acts-for-the-caller-its-credential-names.md" })]
    [InlineData(
        "wpf/src/Presentation/AT.PresentationAppDesktop/README.md",
        "anyPresentation|presentationWpf|dotnetClient",
        9,
        new[] { "docs/features/presentation.md", "src/Presentation/AT.Presentation.Client/", "tests/Presentation/AT.Presentation.Client.UnitTests/", "AppTemplate.slnx" })]
    [InlineData(
        "blazor-wasm/FrameworkSite/README.md",
        "webBlazorWasm|dotnetClient",
        7,
        new[]
        {
            "src/Web/apps/FrameworkSite/", "src/Presentation/AT.Presentation.Client/", "tests/Presentation/AT.Presentation.Client.UnitTests/", "AppTemplate.slnx",
            "docs/getting-started/overview.md", "docs/features/web-frontend.md",
        })]
    [InlineData(
        "worker/src/Presentation/AT.Worker/README.md",
        "jobsWorker",
        12,
        new[] { "docs/features/presentation.md" })]
    public void TheStarterReadme_ListsEveryLineItsStarterWritesOutsideItsFolders(string readme, string symbols, int files, string[] notListed) =>
        ReadmeListsEveryLineWrittenFor(Path.Combine(StartersFolder, readme), readme, symbols, readme.StartsWith(_workerStarter, StringComparison.Ordinal) ? _withoutTheApi : null, files, notListed);

    private const string _withoutTheApi = @"!\(?api\b";

    [Theory]
    [InlineData(
        "src/Web/README.md",
        "anyWeb",
        _appSymbols,
        20,
        new[]
        {
            "src/Web/", ".github/workflows/web.yml", "docs/features/web-frontend.md",
            "tests/EndToEnd/AT.EndToEnd.Tests/WebAppTests.cs", "docs/getting-started/overview.md",
        })]
    [InlineData(
        "src/Web/apps/FrameworkSite/README.md",
        "webFramework|webNodeFramework|webVite|webAngular|webReactRouter|webViteReact|webViteVue|webViteSvelte|webServerRendering|adminAudience",
        "webBlazorWasm|webPlaceholder|dotnetClient",
        2,
        new[]
        {
            "src/Web/", "docs/features/web-frontend.md", "tests/EndToEnd/AT.EndToEnd.Tests/WebAppTests.cs", "docs/getting-started/overview.md",
            "docs/getting-started/first-run.md",
        })]
    [InlineData(
        "src/Web/apps/PlaceholderSite/README.md",
        "webPlaceholder",
        null,
        2,
        new[] { "src/Web/", "docs/features/web-frontend.md", "docs/getting-started/overview.md", "docs/getting-started/first-run.md" })]
    public void TheWebReadme_ListsEveryLineItsAppOrItsWorkspaceWritesOutsideItsFolders(string readme, string symbols, string? elsewhere, int files, string[] notListed) =>
        ReadmeListsEveryLineWrittenFor(Path.Combine(ProjectReferenceGraph.RepositoryRoot, readme), readme, symbols, elsewhere, files, notListed);

    private const string _appSymbols =
        "webPlaceholder|webFramework|webNodeFramework|webVite|webAngular|webReactRouter|webViteReact|webViteVue|webViteSvelte|webBlazorWasm|webServerRendering|dotnetClient";

    private static void ReadmeListsEveryLineWrittenFor(string path, string readme, string symbols, string? elsewhere, int files, string[] notListed)
    {
        string root = ProjectReferenceGraph.RepositoryRoot;
        var listed = File.ReadLines(path)
            .Select(line => line.Trim())
            .ToHashSet(StringComparer.Ordinal);
        var starterSymbol = new Regex($@"(!?)\b(?:{symbols})\b", RegexOptions.None, TimeSpan.FromSeconds(5));
        Regex? listedElsewhere = elsewhere is null ? null : new Regex(elsewhere.StartsWith('!') ? elsewhere : $@"(?<!!\(?)\b(?:{elsewhere})\b", RegexOptions.None, TimeSpan.FromSeconds(5));
        var written = new List<(string File, string Line)>();

        foreach (string file in ProjectFiles(root))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            string text = File.ReadAllText(file);
            if (!text.Contains('\0', StringComparison.Ordinal) && !notListed.Any(whole => relative.StartsWith(whole, StringComparison.Ordinal)))
            {
                written.AddRange(StarterLines(text.Split('\n'), starterSymbol, listedElsewhere).Select(line => (relative, line)));
            }
        }

        written.Select(entry => entry.File).Distinct(StringComparer.Ordinal).Count().ShouldBeGreaterThanOrEqualTo(
            files, "Far fewer files hold lines written for this starter than the template conditions, so this rule is not reading them.");
        written.Where(entry => !listed.Contains(entry.Line)).Select(entry => $"{entry.File}: {entry.Line}").ShouldBeEmpty(
            $"The project template writes these lines outside the starter's folders, and {readme} does not give them to a project " +
            "that adds it with an item template: add each to the edits under 'Joining a project generated without one'.");
    }

    private static readonly Regex _templateOnly = new(@"(?<!!\(?)\btemplateMaintenance\b", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static readonly Regex _condition = new(@"#(?:if|elseif)\s*(\(.*\))", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static readonly Regex _marker = new(@"#(?:if|elseif|else|endif)\b", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static readonly Regex _quoted = new("\"[^\"]*\"", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static readonly Regex _identifier = new(@"\b[A-Za-z_]\w*\b", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static readonly string[] _outsideTheProject =
        [".git", ".nuget", ".template.config", "launcher", "artifacts", "bin", "obj", "node_modules", "TestResults", ".sonarqube", ".vs", ".claude", ".idea", "template"];

    private static string StartersFolder => Path.Combine(ProjectReferenceGraph.RepositoryRoot, ".template.config", "starters");

    private static string PresentationItemManifestPath() =>
        Path.Combine(ProjectReferenceGraph.RepositoryRoot, ".template.config", "presentation", ".template.config", "template.json");

    private static IEnumerable<string> ProjectFiles(string directory) =>
        Directory.EnumerateFiles(directory).Concat(Directory.EnumerateDirectories(directory)
            .Where(folder => !_outsideTheProject.Contains(Path.GetFileName(folder), StringComparer.Ordinal))
            .SelectMany(ProjectFiles));

    private static List<string> StarterLines(string[] lines, Regex starterSymbol, Regex? listedElsewhere = null)
    {
        var branches = new Stack<(string Condition, bool Otherwise)>();
        var written = new List<string>();

        foreach (string line in lines)
        {
            Match marker = _marker.Match(line);

            if (marker.Success)
            {
                Follow(branches, marker.Value, line);
            }
            else if (line.Trim().Length > 0 && WrittenByTheStarterAlone(branches, starterSymbol, listedElsewhere))
            {
                written.Add(line.Trim());
            }
        }

        return written;
    }

    private static bool WrittenByTheStarterAlone(Stack<(string Condition, bool Otherwise)> branches, Regex starterSymbol, Regex? listedElsewhere) =>
        branches.Any(branch => IsStarterBranch(starterSymbol, branch.Condition, branch.Otherwise))
        && !branches.Select(branch => branch.Otherwise ? "!" + branch.Condition : branch.Condition)
            .Any(condition => _templateOnly.IsMatch(condition) || listedElsewhere?.IsMatch(condition) == true);

    private static void Follow(Stack<(string Condition, bool Otherwise)> branches, string marker, string line)
    {
        if (marker == "#if")
        {
            branches.Push((_condition.Match(line).Groups[1].Value, false));
        }
        else if (marker == "#elseif" && branches.TryPop(out _))
        {
            branches.Push((_condition.Match(line).Groups[1].Value, false));
        }
        else if (marker == "#else" && branches.TryPop(out var branch))
        {
            branches.Push((branch.Condition, true));
        }
        else
        {
            branches.TryPop(out _);
        }
    }

    private static bool IsStarterBranch(Regex starterSymbol, string condition, bool otherwise) =>
        starterSymbol.Match(condition) is { Success: true } match && (match.Groups[1].Value == "!") == otherwise;

    private static string? Facet(JsonElement symbol, string facet) =>
        symbol.TryGetProperty(facet, out var value) ? value.ToString() : null;

    private static string[] ChoiceValues(JsonElement symbol) =>
        symbol.TryGetProperty("choices", out var choices)
            ? [.. choices.EnumerateArray().Select(choice => choice.GetProperty("choice").GetString()!)]
            : [];

    private static JsonNode? StarterModifiers(JsonElement manifest, string startersSource, string starter) =>
        JsonNode.Parse(manifest.GetProperty("sources").EnumerateArray()
            .Single(source => Text(source, "source") == $"{startersSource}{starter}/")
            .GetProperty("modifiers").GetRawText());

    private static bool ChoosesTheStarterOrReadsTheProject(JsonElement symbol, string facet) =>
        Facet(symbol, facet) is { } value
        && (value.Contains("framework == ", StringComparison.Ordinal) || value.Contains("hasApi", StringComparison.Ordinal));

    private static string PresentationDescriptor(string presentation) =>
        Path.Combine(StarterProject(presentation), "scaffold.json");

    private static string StarterProject(string presentation) =>
        Path.Combine(StartersFolder, presentation, "src", "Presentation") is var projects && Directory.Exists(projects)
            ? Directory.GetDirectories(projects).Order(StringComparer.Ordinal).FirstOrDefault(project => File.Exists(Path.Combine(project, "scaffold.json"))) ?? projects
            : projects;

    private const int _longestPathBudget = 170;

    private const int _longestEntryBudget = 207;

    private const int _nugetPathWarning = 167;

    private static readonly string[] _scratchFolders = [".git", "bin", "obj", "node_modules", "TestResults", "artifacts", ".claude", ".vs", ".idea", ".vscode"];

    private const string _folderPrefixSymbol = "projectFolderPrefix";

    private const string _folderPrefixAnyCaseSymbol = "projectFolderPrefixAnyCase";

    private static readonly HashSet<string> _foldersNoTemplateWrites =
        new([.. _outsideTheProject.Where(folder => folder != ".template.config"), "plans"], StringComparer.Ordinal);

    private static readonly char[] _nameStarts = [.. "_abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ"];

    private static readonly char[] _nameCharacters = [.. _nameStarts, .. "0123456789."];

    private static readonly char[] _anyNameCharacters = [.. _nameCharacters, .. "-@[`{\u00e9\u00c9\u0131\u017f\u212a"];

    private const string _nameLacksALeadingLetter = "nameLacksALeadingLetter";

    private static readonly string[] _productNames =
        ["AppTemplate", "Acme.Shop", "Contoso.OrderManagement", "acme.shop", "MyCompany.MyProject", "My_App", "acme-shop", "Acme.shop2", "Acme.2024", "ACME.Shop"];

    private static readonly string[] _treesHoldingProjects = ["src", "tests", ".template.config"];

    private static readonly string[] _generatedTemplateFolders =
        [".template.config/starters", ".template.config/variants", ".template.config/webpackage/content"];

    private static readonly Regex _segmentInitials = new(@"private static string SegmentInitials\(string segment\) =>([^;]+);", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static IEnumerable<string> Collisions(string relative, string text, string token, string[] suffixes)
    {
        for (int at = text.IndexOf(token, StringComparison.Ordinal); at >= 0; at = text.IndexOf(token, at + 1, StringComparison.Ordinal))
        {
            string after = text[(at + token.Length)..];
            bool startsAWord = at == 0 || !(char.IsAsciiLetterOrDigit(text[at - 1]) || text[at - 1] == '_');
            bool namesAProject = suffixes.Any(suffix => after.StartsWith(suffix, StringComparison.Ordinal)
                && (after.Length == suffix.Length || !(char.IsAsciiLetterOrDigit(after[suffix.Length]) || after[suffix.Length] == '_')));

            if (!startsAWord || !namesAProject)
            {
                int line = text.AsSpan(0, at).Count('\n') + 1;
                yield return $"{relative}:{line}: {text[Math.Max(0, at - 12)..Math.Min(text.Length, at + token.Length + 16)].ReplaceLineEndings(" ")}";
            }
        }
    }

    private static string SegmentInitials(string relative) =>
        _segmentInitials.Match(File.ReadAllText(Path.Combine(ProjectReferenceGraph.RepositoryRoot, relative))) is { Success: true } match
            ? string.Join(' ', match.Groups[1].Value.Split((char[])[' ', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
            : string.Empty;

    private const string _apiChoice = "api";

    private const string _workerStarter = "worker";

    private static readonly string[] _sharedDesktopCode =
    [
        "src/Presentation/AT.PresentationAppDesktop/Common",
        "src/Presentation/AT.PresentationAppDesktop/appsettings.json",
        "tests/Presentation/AT.PresentationAppDesktop.UnitTests/Common",
        "tests/Presentation/AT.PresentationAppDesktop.UnitTests/TestSupport",
        "tests/Presentation/AT.PresentationAppDesktop.UnitTests/ViewModels/MainViewModelTests.cs",
    ];

    private static string[] SharedDesktopCode(string starter)
    {
        string root = Path.Combine(StartersFolder, starter);

        return [.. _sharedDesktopCode
            .Select(path => Path.Combine(root, path))
            .SelectMany(path => Directory.Exists(path) ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories) : File.Exists(path) ? [path] : [])
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];
    }

    private static readonly Regex _combination = new(@"\([^()]*&&[^()]*\)", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static readonly Regex _declaredNamespace = new(@"^namespace AppTemplate\.(\S+);$", RegexOptions.None, TimeSpan.FromSeconds(5));

    private static IEnumerable<JsonElement> MovedSources(List<JsonElement> sources) =>
        sources.Where(source => Text(source, "condition") == "(jobsWorker)"
            && source.TryGetProperty("rename", out JsonElement rename)
            && rename.EnumerateObject().Any(entry => entry.Value.GetString()!.StartsWith(ProjectFolders.FolderOf("AppTemplate.Worker"), StringComparison.Ordinal)));

    private static IEnumerable<string> MovedFiles(JsonElement source)
    {
        string root = Path.Combine(ProjectReferenceGraph.RepositoryRoot, Text(source, "source")![2..]);

        return source.GetProperty("include").EnumerateArray()
            .Select(include => include.GetString()!.Replace("/**", string.Empty, StringComparison.Ordinal))
            .SelectMany(folder => folder.Contains('*', StringComparison.Ordinal)
                ? Directory.GetDirectories(Path.Combine(root, folder.Split("/*/")[0]))
                    .Select(feature => Path.Combine(feature, folder.Split("/*/")[1]))
                : [Path.Combine(root, folder)])
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories));
    }

    private static string[] WebStarters() =>
        [.. Directory.GetDirectories(StartersFolder)
            .Where(folder => File.Exists(Path.Combine(folder, "FrameworkSite", "scaffold.json")))
            .Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];

    private static string[] PresentationStarters() =>
        [.. Directory.GetDirectories(StartersFolder)
            .Select(Path.GetFileName).OfType<string>()
            .Where(folder => File.Exists(PresentationDescriptor(folder)))
            .Order(StringComparer.Ordinal)];

    private static List<string> ChosenValues(string expression, string option) =>
        [.. Regex.Matches(expression, $@"\b{option} == ""([^""]+)""", RegexOptions.None, TimeSpan.FromSeconds(5)).Select(match => match.Groups[1].Value)];

    private static bool Reaches(Dictionary<string, string> computed, string from, string target)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([from]);

        while (pending.TryPop(out string? symbol))
        {
            if (symbol == target)
            {
                return true;
            }

            if (!seen.Add(symbol) || !computed.TryGetValue(symbol, out string? expression))
            {
                continue;
            }

            foreach (Match name in Regex.Matches(expression, @"\b[A-Za-z]\w*\b", RegexOptions.None, TimeSpan.FromSeconds(5)))
            {
                pending.Push(name.Value);
            }
        }

        return false;
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() : null;

    private static IEnumerable<JsonProperty> RecordedOptions(JsonElement symbols) =>
        symbols.EnumerateObject()
            .Where(symbol => Text(symbol.Value, "type") == "parameter" && !IsRefusal(symbol.Value))
            .Where(symbol => !symbol.Name.StartsWith("web", StringComparison.Ordinal) && !symbol.Name.StartsWith("presentation", StringComparison.Ordinal));

    private static bool IsRefusal(JsonElement symbol) =>
        symbol.TryGetProperty("choices", out var choices)
        && choices.EnumerateArray().All(choice => choice.GetProperty("choice").GetString() == "refused");

    private static List<string> UnconditionalExcludes()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(ManifestPath()));

        return [.. manifest.RootElement.GetProperty("sources")[0].GetProperty("modifiers").EnumerateArray()
            .Where(modifier => !modifier.TryGetProperty("condition", out _))
            .SelectMany(modifier => modifier.GetProperty("exclude").EnumerateArray().Select(pattern => pattern.GetString()!))];
    }

    private static IEnumerable<string> PackedFiles(string root, string relative)
    {
        string directory = Path.Combine(root, relative);

        foreach (string file in Directory.EnumerateFiles(directory))
        {
            yield return Path.Combine(relative, Path.GetFileName(file)).Replace('\\', '/');
        }

        foreach (string folder in Directory.EnumerateDirectories(directory).Where(folder => !_scratchFolders.Contains(Path.GetFileName(folder), StringComparer.Ordinal)))
        {
            foreach (string file in PackedFiles(root, Path.Combine(relative, Path.GetFileName(folder))))
            {
                yield return file;
            }
        }
    }

    private static Regex GlobPattern(string pattern) =>
        new(
            "^" + Regex.Escape(pattern.Replace('\\', '/'))
                .Replace(@"\*\*/", "(?:.*/)?", StringComparison.Ordinal)
                .Replace(@"\*\*", ".*", StringComparison.Ordinal)
                .Replace(@"\*", "[^/]*", StringComparison.Ordinal) + "$",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

    private static List<string> PackageExcludes()
    {
        string project = Path.Combine(ProjectReferenceGraph.RepositoryRoot, ".template.config", "TemplatePackage.csproj");

        return [.. XDocument.Load(project)
            .Descendants("TemplateExclude")
            .SelectMany(item => item.Attribute("Include")!.Value.Split(';', StringSplitOptions.RemoveEmptyEntries))
            .Select(pattern => pattern.Replace("$(TemplateRoot)", string.Empty, StringComparison.Ordinal))];
    }

    private static List<string> ConditionedBlocks(string[] lines, string symbol)
    {
        string opening = "#" + $"if ({symbol})";
        string closing = "#" + "endif";
        var blocks = new List<string>();
        List<string>? block = null;

        foreach (string line in lines)
        {
            if (line.Trim() == opening)
            {
                block = [];
            }
            else if (block is not null && line.Trim() == closing)
            {
                blocks.Add(string.Join('\n', block).TrimEnd());
                block = null;
            }
            else
            {
                block?.Add(line);
            }
        }

        return blocks;
    }

    private static List<string> AddedBlocksUnder(string[] lines, string bullet)
    {
        int[] starts = [.. Enumerable.Range(0, lines.Length).Where(index => lines[index].StartsWith(bullet, StringComparison.Ordinal))];
        starts.ShouldNotBeEmpty($"src/Web/README.md has no bullet starting '{bullet}'.");

        return [.. starts.Select(start => FencedBlocksUnder(lines, start).Last())];
    }

    private static List<string> FencedBlocksUnder(string[] lines, int start)
    {

        var blocks = new List<string>();
        List<string>? block = null;
        int indent = 0;

        foreach (string line in lines.Skip(start + 1).TakeWhile(line => line.Length == 0 || char.IsWhiteSpace(line[0])))
        {
            string trimmed = line.TrimStart();
            if (block is null && trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                block = [];
                indent = line.Length - trimmed.Length;
            }
            else if (block is not null && trimmed == "```")
            {
                blocks.Add(string.Join('\n', block).TrimEnd());
                block = null;
            }
            else
            {
                block?.Add(line.Length >= indent ? line[indent..] : line.TrimStart());
            }
        }

        return blocks;
    }

    private static string ManifestPath()
    {
        string manifest = Path.Combine(ProjectReferenceGraph.RepositoryRoot, ".template.config", "template.json");

        File.Exists(manifest).ShouldBeTrue(
            $"'{manifest}' does not exist, so this repository packages a template `dotnet new` cannot read.");

        return manifest;
    }

    private static HashSet<string> DeclaredGuids()
    {
        string manifest = ManifestPath();

        using var document = JsonDocument.Parse(File.ReadAllText(manifest));

        return [.. document.RootElement
            .GetProperty("guids")
            .EnumerateArray()
            .Select(element => element.GetString()!)];
    }

    private static List<(string Project, string Guid)> UserSecretsGuids()
    {
        string root = ProjectReferenceGraph.RepositoryRoot;
        string[] folders = ["src", "tests", Path.Combine(".template.config", "starters")];

        return [.. folders
            .Select(folder => Path.Combine(root, folder))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.csproj", SearchOption.AllDirectories))
            .Where(project => !project.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(project => XDocument.Load(project)
                .Descendants("UserSecretsId")
                .Select(id => (Path.GetRelativePath(root, project).Replace('\\', '/'), id.Value.Trim())))
            .Order()];
    }

    private static HashSet<string> SolutionGuids()
    {
        string solution = Path.Combine(ProjectReferenceGraph.RepositoryRoot, "AppTemplate.slnx");

        File.Exists(solution).ShouldBeTrue($"The solution was not found at '{solution}'.");

        return [.. _guid
            .Matches(File.ReadAllText(solution))
            .Select(match => match.Value)];
    }
}

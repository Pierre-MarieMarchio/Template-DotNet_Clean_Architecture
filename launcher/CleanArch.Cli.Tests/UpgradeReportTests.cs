using System.IO.Compression;
using System.Text.Json.Nodes;
using CleanArch.Cli.UpgradeReport;
using CleanArch.Tooling.Failures;
using CleanArch.Tooling.Manifests;
using Shouldly;
using Xunit;

namespace CleanArch.Cli.Tests;

public sealed class UpgradeReportTests : IDisposable
{
    private const string _heldGuid = "77777777-7777-7777-7777-777777777777";
    private const string _otherHeldGuid = "88888888-8888-8888-8888-888888888888";
    private const string _changelog = "# Changelog\n\nIntro.\n\n## [2.2.0](https://example.invalid) (2026-12-01)\n\n* c\n\n## 2.1.0 (2026-11-20)\n\n* b\n\n## 2.0.0 (2026-11-10)\n\n* a\n";

    private static readonly string[] _drawnGuids = ["aaaaaaaa-0000-4000-8000-000000000001", "bbbbbbbb-0000-4000-8000-000000000002", "cccccccc-0000-4000-8000-000000000003"];

    private static readonly Release[] _catalog = [new("2.0.0", null), new("2.1.0", null), new("2.2.0-rc.1", null), new("1.0.0", null)];

    private readonly string _scratch = Directory.CreateTempSubdirectory("upgrade-report-tests-").FullName;

    public void Dispose() => Directory.Delete(_scratch, recursive: true);

    [Fact]
    public void AFileUnderARootFolderCasedOtherwise_IsPairedAcrossTheTrees_EachReadAtItsOwnPath()
    {
        Finding moved = Classified().Findings.Single(finding => finding.Path == "Src/Domain/Moved.cs");

        moved.Older.ShouldBe("Src/Domain/Moved.cs");
        moved.Newer.ShouldBe("src/Domain/Moved.cs");
    }

    [Theory]
    [InlineData("same.md", "-")]
    [InlineData("take.md", "Take")]
    [InlineData("merge.md", "Merge")]
    [InlineData("deleted.md", "Merge, noted")]
    [InlineData("added.md", "Added")]
    [InlineData("clash.md", "Added, noted")]
    [InlineData("removed.md", "Removed")]
    [InlineData("removed-changed.md", "Removed, noted")]
    [InlineData("own.md", "-")]
    [InlineData("line-endings.md", "Take")]
    [InlineData("real-change.md", "Merge")]
    [InlineData("Usings.cs", "Take")]
    [InlineData("UsingRemoved.cs", "Merge")]
    [InlineData("Other.sln", "-")]
    [InlineData("Moved.sln", "Take")]
    [InlineData("Drawn.csproj", "-")]
    [InlineData("DrawnTake.csproj", "Take")]
    [InlineData("HeldChanged.csproj", "Take")]
    [InlineData("HeldEdited.csproj", "Merge")]
    [InlineData("DrawnTwo.csproj", "Merge")]
    [InlineData("Sample.Shop.sln", "Merge, noted")]
    [InlineData("Sample.Shop.slnx", "Merge, noted")]
    [InlineData(".config/dotnet-tools.json", "Merge, noted")]
    [InlineData("src/Web/apps/Plain/package.json", "Merge, noted")]
    [InlineData("docker-compose.yml", "Merge, noted")]
    [InlineData("cleanarch.json", "-")]
    [InlineData("src/Presentation/SS.Presentation.Client/Generated/ApiClient.cs", "-")]
    [InlineData("src/Web/apps/Site/src/main.ts", "Merge, noted")]
    [InlineData("src/Web/apps/Site/Dockerfile", "Take")]
    [InlineData("logo.png", "Merge")]
    [InlineData("Src/Domain/Moved.cs", "Take")]
    [InlineData("tests/Kept.cs", "Merge")]
    public void AFile_IsClassifiedByWhatTheTargetChangedAndWhatTheProjectChanged(string path, string expected)
    {
        Finding[] findings = [.. Classified().Findings.Where(finding => finding.Path == path)];

        string.Join(" | ", findings.Select(finding => finding.Note is null ? $"{finding.Kind}" : $"{finding.Kind}, noted"))
            .ShouldBe(expected == "-" ? string.Empty : expected);
    }

    [Fact]
    public void AFileNoGenerationCompares_IsListedApart_WithWhatWritesIt()
    {
        Comparison comparison = Classified();
        string[] fromBootstrap = [.. comparison.Bootstrap.Values.SelectMany(paths => paths)];

        comparison.Own.ShouldBe(["own.md"]);
        comparison.Provenance.ShouldNotBeNull();
        fromBootstrap.ShouldContain("src/Infrastructure/SS.Infrastructure.Persistence/Migrations/1_InitialCreate.cs");
        fromBootstrap.ShouldContain("tests/Integration/SS.Api.IntegrationTests/OpenApi/v1.json");
        comparison.Bootstrap.ShouldContain(group => group.Key == "dotnet cleanarch add web-app --name Site" && group.Value.Contains("src/Web/apps/Site/vite.config.ts"));
        comparison.Scaffolds.ShouldHaveSingleItem().ShouldStartWith("src/Web/apps/Site/scaffold.json: changed by the target version");
        CanonicalText.CSharp("using A;\nnamespace N;\nstring s = \"\"\"\nusing B;\nusing C;\n\"\"\";\n")
            .ShouldNotBe(CanonicalText.CSharp("using A;\nnamespace N;\nstring s = \"\"\"\nusing C;\nusing B;\n\"\"\";\n"), "Lines of a string literal after the header were ordered as using directives.");
    }

    [Theory]
    [InlineData("removes the option --web-name, which cleanarch.json records; it adds --site-name, and a rename is never assumed", true)]
    [InlineData("adds the option --site-name, generated here under its default (Site)", true)]
    [InlineData("no longer offers none for --auth", true)]
    [InlineData("changes the default of --auth from local to external", true)]
    [InlineData("guard", false)]
    public void TheOptionChanges_NameWhatTheTargetChanged_AndNeverARefusalGuard(string fragment, bool reported)
    {
        TemplatePackage older = Package(new JsonObject { ["auth"] = Choice("local", "local", "external", "none"), ["webName"] = Text("Site") });
        TemplatePackage newer = Package(new JsonObject { ["auth"] = Choice("external", "local", "external"), ["siteName"] = Text("Site"), ["guard"] = Guard() });

        string[] changes = [.. OptionChanges.Between(older, newer, Project(options: [("auth", "none"), ("webName", "Shop")]), "2.1.0")];

        changes.Any(change => change.Contains(fragment, StringComparison.Ordinal)).ShouldBe(reported, string.Join(" | ", changes));
    }

    [Fact]
    public void AnOptionTheVersionLacks_IsLeftOutAndNoted_AndAValueItNoLongerOffers_IsRefused()
    {
        PackagedTemplate main = Package(new JsonObject { ["auth"] = Choice("external", "local", "external"), ["siteName"] = Text("Site") }).Templates[PackagedTemplate.Main];
        var notes = new List<string>();

        Generator.Arguments(main, [("siteName", "Shop"), ("webName", "Shop")], "2.1.0", notes).ShouldBe(["--site-name", "Shop"]);
        notes.ShouldHaveSingleItem().ShouldContain("has no option webName");
        Should.Throw<LauncherException>(() => Generator.Arguments(main, [("auth", "none")], "2.1.0", notes)).Message.ShouldContain("does not offer none for --auth");
    }

    [Theory]
    [InlineData("210D431B-A78B-4D2F-B762-4ED3E3EA9025", true)]
    [InlineData("AC1156F7-BB77-4DB8-B28F-24EEBCCA1E5C", false)]
    public void APackage_DeclaringAPostActionBeyondManualInstructions_IsRefused(string actionId, bool refused)
    {
        TemplatePackage package = Package([], [new JsonObject { ["actionId"] = actionId }]);

        Action refuse = () => Generator.RefuseRunningPostActions(package, "2.1.0");

        if (refused)
        {
            Should.Throw<LauncherException>(refuse).Message.ShouldContain($"cleanarch-webapi {actionId}");
        }
        else
        {
            Should.NotThrow(refuse);
        }
    }

    [Theory]
    [InlineData("2.0.0", null, "2.1.0")]
    [InlineData("2.0.0", "2.2.0-rc.1", "2.2.0-rc.1")]
    [InlineData("2.1.0", null, "No stable version of P is newer than 2.1.0")]
    [InlineData("2.1.0", "2.0.0", "--to 2.0.0 is not newer than 2.1.0")]
    [InlineData("2.1.0", "2.1.0", "--to 2.1.0 is not newer than 2.1.0")]
    [InlineData("2.0.0", "3.0.0", "Version 3.0.0 of P is not found")]
    [InlineData("2.0.0", "2.x", "--to '2.x' is not a version")]
    [InlineData("1.5.0", null, "Version 1.5.0 of P, the one cleanarch.json records, is not found")]
    public void TheTarget_IsTheLatestStableVersionOrTheOneNamed_NewerThanTheProjects(string current, string? to, string expected)
    {
        string chosen;
        try
        {
            chosen = Releases.Choose(current, to, _catalog, "P").To.Version;
        }
        catch (LauncherException refusal)
        {
            chosen = refusal.Message;
        }

        chosen.ShouldStartWith(expected);
    }

    [Theory]
    [InlineData("2.0.0-rc.2", "2.0.0-rc.10", -1)]
    [InlineData("2.0.0", "2.0.0-rc.10", 1)]
    [InlineData("2.0.0-beta", "2.0.0-rc.1", -1)]
    [InlineData("2.0.0-rc", "2.0.0-rc.1", -1)]
    [InlineData("1.10.0", "1.9.0", 1)]
    [InlineData("2.0.0-1", "2.0.0-alpha", -1)]
    public void Versions_AreOrderedAsSemanticVersioningOrdersThem(string left, string right, int sign) =>
        Math.Sign(SemanticVersion.Compare(left, right)).ShouldBe(sign);

    [Fact]
    public void ThePlan_GeneratesWhatTheTemplateExpresses_AndReplaysTheRestInTheRecordedOrder()
    {
        GenerationPlan plan = GenerationPlan.Of(Project(
            options: [("auth", "external"), ("files", "false")],
            web: new WebWorkspace("sample-shop", [new WebApp("Site", "vite-react", "public", "spa"), new WebApp("BackOffice", "angular", "admin", "ssr")], ["tokens"]),
            presentation: [new PresentationProject("Api", "api"), new PresentationProject("Worker", "worker"), new PresentationProject("Admin", "blazor-server")]));

        string.Join(" ", plan.Generation.Select(value => value.Symbol + "=" + value.Value))
            .ShouldBe("auth=external files=false web=vite-react webName=Site webAudience=public webRendering=spa presentation=api presentation=worker");
        plan.Replays.Select(replay => replay.Name).ShouldBe(["BackOffice", "tokens", "Admin"]);
        plan.Replays[0].Values.ShouldContain(("rendering", "ssr"));
        plan.Replays[0].ProjectOptions.ShouldContain(("auth", "external"));
    }

    [Theory]
    [InlineData("sample-shop", null, "Shop")]
    [InlineData("sample-shop", 8083, "Shop port=8083")]
    [InlineData("custom", null, "Site Shop")]
    public void AWebAppAfterTheFirst_OrUnderAnotherScope_IsReplayed_OnThePortItsRecordHolds(string scope, int? port, string replayed)
    {
        GenerationPlan plan = GenerationPlan.Of(Project(web: new WebWorkspace(scope, [new WebApp("Site", "vite-react", "public", "spa"), new WebApp("Shop", "angular", "public", "spa", port)], [])));

        string.Join(" ", plan.Replays.Select(replay => string.Join(" ", replay.Values.Where(value => value.Symbol == "port").Select(value => $"port={value.Value}").Prepend(replay.Name))))
            .ShouldBe(replayed);
    }

    [Theory]
    [InlineData("No stable version of P is newer than 2.1.0", "--source", "{feed}")]
    [InlineData("--to 2.0.0 is not newer than 2.1.0", "--source", "{feed}", "--to", "2.0.0")]
    [InlineData("Version 9.9.9 of P is not found", "--source", "{feed}", "--to", "9.9.9")]
    [InlineData("Version 2.1.0 of P, the one cleanarch.json records, is not found", "--source", "{feed}/P.2.0.0.nupkg")]
    [InlineData("is neither a .nupkg nor a folder", "--source", "{feed}/none")]
    [InlineData("is inside the project", "--source", "{feed}", "--json", "{root}/report.json")]
    [InlineData("is inside the project", "--source", "{feed}", "--keep", "{root}/kept")]
    [InlineData("is inside the project", "--source", "{feed}", "--keep", "{root}")]
    [InlineData("is the checkout the package is packed from", "--source", "{feed}", "--checkout")]
    [InlineData("Unrecognized command or argument '--unknown'", "--unknown", "x")]
    public void WhatTheReportCannotCompare_IsRefused_BeforeAnythingRunsOrIsWritten(string refusal, params string[] arguments)
    {
        using var project = new SampleProject("""{ "template": { "package": "P", "version": "2.1.0" }, "name": "Sample.Shop", "options": {} }""");
        string feed = Feed("2.0.0", "2.1.0");
        if (arguments.Contains("--checkout"))
        {
            Directory.CreateDirectory(Path.Combine(project.Root, ".template.config"));
            File.WriteAllText(Path.Combine(project.Root, ".template.config", "template.json"), "{}");
        }

        string[] before = [.. Directory.EnumerateFileSystemEntries(project.Root, "*", SearchOption.AllDirectories)];
        var calls = new List<ExternalCall>();
        using var output = new StringWriter();
        using var error = new StringWriter();
        string[] command = ["upgrade-report", .. arguments.Where(argument => argument != "--checkout").Select(argument => argument.Replace("{feed}", feed, StringComparison.Ordinal).Replace("{root}", project.Root, StringComparison.Ordinal))];

        int exitCode = BuiltInCommands.Run(command, project.Root, output, error, new FakeProcesses((call, _, _) =>
        {
            calls.Add(call);
            return CleanArchCommand.Succeeded;
        }));

        exitCode.ShouldBe(CleanArchCommand.Failed);
        error.ToString().ShouldContain(refusal);
        calls.ShouldBeEmpty();
        Directory.EnumerateFileSystemEntries(project.Root, "*", SearchOption.AllDirectories).ShouldBe(before, ignoreOrder: true);
    }

    [Fact]
    public void APackage_HoldsTheGuidsItsFilesWrite_DrawsGuidsOnlyWhereATemplateDeclaresThem_AndNamesATemplateByItsFirstShortName()
    {
        TemplatePackage drawing = Packed(
            "drawing",
            "{ \"identity\": \"P\", \"shortName\": \"p\", \"symbols\": {}, \"guids\": [\"11111111-2222-3333-4444-555555555555\"] }",
            "<UserSecretsId>11111111-2222-3333-4444-555555555555</UserSecretsId><Type>{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}</Type>");
        TemplatePackage plain = Packed("plain", "{ \"identity\": \"P\", \"shortName\": [\"p\", \"q\"], \"symbols\": {} }", "<Type>{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}</Type>");

        drawing.DrawsGuids.ShouldBeTrue();
        drawing.HeldGuids.ShouldContain("fae04ec0-301f-11d3-bf4b-00c04f79efbc");
        drawing.HeldGuids.ShouldContain("11111111-2222-3333-4444-555555555555");
        drawing.HeldGuids.Count.ShouldBe(2);
        plain.DrawsGuids.ShouldBeFalse();
        plain.Require("P", "1.0.0").ShortName.ShouldBe("p");
    }

    [Theory]
    [InlineData("{ \"identity\": \"P\", \"symbols\": {} }", "The template has no shortName.")]
    [InlineData("{ \"identity\": \"P\", ", "template.json")]
    public void APackage_WhoseTemplateCannotBeRead_IsRefused_NamingTheFileAndWhy(string manifest, string why)
    {
        string message = Should.Throw<LauncherException>(() => Packed("unreadable", manifest, string.Empty)).Message;

        message.ShouldContain("unreadable.nupkg");
        message.ShouldContain("content/.template.config/template.json");
        message.ShouldContain(why);
    }

    [Theory]
    [InlineData("2.0.0", "2.1.0", "## 2.1.0 (2026-11-20)\n\n* b")]
    [InlineData("2.0.0", "2.2.0", "## [2.2.0](https://example.invalid) (2026-12-01)\n\n* c\n\n## 2.1.0 (2026-11-20)\n\n* b")]
    [InlineData("2.2.0", "2.3.0", "CHANGELOG.md holds no entry after 2.2.0 up to 2.3.0.")]
    public void TheChangelog_QuotesTheEntriesAfterTheProjectsVersionUpToTheTarget(string from, string to, string expected) =>
        Changelog.Between(_changelog, from, to).ShouldBe(expected);

    private Comparison Classified()
    {
        string older = Path.Combine(_scratch, "older");
        string newer = Path.Combine(_scratch, "newer");
        string project = Path.Combine(_scratch, "project");
        void Three(string path, string? inOlder, string? inNewer, string? inProject)
        {
            foreach ((string root, string? text) in new[] { (older, inOlder), (newer, inNewer), (project, inProject) }.Where(tree => tree.Item2 is not null))
            {
                string full = Path.Combine(root, path);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, text);
            }
        }

        static string Secrets(string guid, string rest = "") => $"<UserSecretsId>{guid}</UserSecretsId>\n{rest}";

        void One(string root, string path, string text)
        {
            string full = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
        }

        One(older, "Src/Domain/Moved.cs", "a\n");
        One(newer, "src/Domain/Moved.cs", "b\n");
        One(project, "Src/Domain/Moved.cs", "a\n");
        One(older, "Tests/Kept.cs", "a\n");
        One(newer, "tests/Kept.cs", "b\n");
        One(project, "tests/Kept.cs", "c\n");
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
        Three("Sample.Shop.slnx", "a\n", "b\n", "a\n");
        Three("Other.sln", Solution("1", "2", "src\\A.csproj"), Solution("3", "4", "src\\A.csproj"), Solution("5", "6", "src\\A.csproj"));
        Three("Moved.sln", Solution("1", "2", "src\\A.csproj"), Solution("3", "4", "src\\B.csproj"), Solution("5", "6", "src\\A.csproj"));
        Three("Drawn.csproj", Secrets(_drawnGuids[0]), Secrets(_drawnGuids[1]), Secrets(_drawnGuids[2]));
        Three("DrawnTake.csproj", Secrets(_drawnGuids[0], "<A>1</A>\n"), Secrets(_drawnGuids[1], "<A>2</A>\n"), Secrets(_drawnGuids[2].ToUpperInvariant(), "<A>1</A>\n"));
        Three("HeldChanged.csproj", Secrets(_heldGuid), Secrets(_otherHeldGuid), Secrets(_heldGuid));
        Three("HeldEdited.csproj", Secrets(_heldGuid, "<A>1</A>\n"), Secrets(_heldGuid, "<A>2</A>\n"), Secrets(_drawnGuids[2], "<A>1</A>\n"));
        Three("DrawnTwo.csproj", Secrets(_drawnGuids[0], _drawnGuids[1] + "\n<A>1</A>\n"), Secrets(_drawnGuids[1], _drawnGuids[0] + "\n<A>2</A>\n"), Secrets(_drawnGuids[2], _drawnGuids[2] + "\n<A>1</A>\n"));
        Three(".config/dotnet-tools.json", "{}\n", "{ }\n", "{}\n");
        Three("docker-compose.yml", "a\n", "b\n", "a\n");
        Three("cleanarch.json", "{}\n", "{ }\n", "{}\n");
        Three("src/Infrastructure/SS.Infrastructure.Persistence/Migrations/1_InitialCreate.cs", null, null, "class M;\n");
        Three("src/Presentation/SS.Presentation.Client/Generated/ApiClient.cs", "a\n", "b\n", "c\n");
        Three("tests/Integration/SS.Api.IntegrationTests/OpenApi/v1.json", null, null, "{}\n");
        Three("src/Web/apps/Site/scaffold.json", "{\"version\":\"1\"}\n", "{\"version\":\"2\"}\n", null);
        Three("src/Presentation/SS.Portal/scaffold.json", "{\"kind\":\"dotnet\"}\n", "{\"kind\":\"dotnet\"}\n", null);
        Three("src/Web/apps/Site/overlay/src/main.ts", "a\n", "b\n", null);
        Three("src/Web/apps/Site/src/main.ts", null, null, "a\n");
        Three("src/Web/apps/Site/vite.config.ts", null, null, "x\n");
        Three("src/Web/apps/Site/Dockerfile", "a\n", "b\n", "a\n");
        Three("src/Web/apps/Plain/package.json", "{}\n", "{ }\n", "{}\n");
        Three(".gitattributes", "* text=auto\n*.png binary\n", "* text=auto\n*.png binary\n", "* text=auto\n*.png binary\n");
        Three("logo.png", "a\r\nb", "a\r\nc", "a\nb");

        var joined = new Dictionary<string, string>(StringComparer.Ordinal) { ["docker-compose.yml"] = "src/Web/apps/Other" };
        return TreeComparison.Compare(
            new Trees(older, newer, project), [.. ProjectTree.Files(project)], "Sample.Shop", joined, new HashSet<string>([_heldGuid, _otherHeldGuid], StringComparer.OrdinalIgnoreCase));
    }

    private static string Solution(string folder, string project, string path)
    {
        string folderGuid = string.Concat(Enumerable.Repeat(folder, 8)) + "-1111-1111-1111-" + string.Concat(Enumerable.Repeat(folder, 12));
        string projectGuid = string.Concat(Enumerable.Repeat(project, 8)) + "-2222-2222-2222-" + string.Concat(Enumerable.Repeat(project, 12));

        return $"Project(\"{{2150E333-8FDC-42A3-9474-1A3956D46DE8}}\") = \"src\", \"src\", \"{{{folderGuid}}}\"\nEndProject\n"
            + $"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"A\", \"{path}\", \"{{{projectGuid}}}\"\nEndProject\n"
            + $"\t\t{{{projectGuid}}} = {{{folderGuid}}}\n";
    }

    private static JsonObject Choice(string defaultValue, params string[] choices) => new()
    {
        ["type"] = "parameter",
        ["datatype"] = "choice",
        ["defaultValue"] = defaultValue,
        ["choices"] = new JsonArray([.. choices.Select(choice => (JsonNode)new JsonObject { ["choice"] = choice })]),
    };

    private static JsonObject Guard()
    {
        JsonObject guard = Choice("none", "refused");
        guard["isRequired"] = "auth == \"none\"";
        return guard;
    }

    private static JsonObject Text(string defaultValue) => new() { ["type"] = "parameter", ["datatype"] = "text", ["defaultValue"] = defaultValue };

    private static TemplatePackage Package(JsonObject symbols, JsonArray? postActions = null) => new(
        "x",
        new Dictionary<string, PackagedTemplate>(StringComparer.Ordinal)
        {
            [PackagedTemplate.Main] = new(
                PackagedTemplate.Main,
                TemplateManifest.Read(
                    new JsonObject { ["shortName"] = "cleanarch-webapi", ["symbols"] = symbols, ["postActions"] = postActions ?? [] }.ToJsonString(),
                    """{ "symbolInfo": { "webName": { "longName": "web-name" }, "siteName": { "longName": "site-name" } } }""",
                    """{ "symbolInfo": [ { "id": "auth", "isVisible": true } ] }""")),
        },
        null,
        null,
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        DrawsGuids: false);

    private static Provenance Project(
        (string Key, string Value)[]? options = null,
        WebWorkspace? web = null,
        PresentationProject[]? presentation = null) =>
        new("P", "2.0.0", "Sample.Shop", options ?? [], web, presentation ?? [new PresentationProject("Api", "api")]);

    private string Feed(params string[] versions)
    {
        string feed = Path.Combine(_scratch, "feed");
        foreach (string version in versions)
        {
            Zip(Path.Combine(feed, $"P.{version}.nupkg"), ("P.nuspec", $"<package><metadata><id>P</id><version>{version}</version></metadata></package>"));
        }

        return feed;
    }

    private TemplatePackage Packed(string name, string manifest, string project)
    {
        string nupkg = Path.Combine(_scratch, name + ".nupkg");
        Zip(
            nupkg,
            ("P.nuspec", "<package><metadata><id>P</id><version>1.0.0</version></metadata></package>"),
            ("content/.template.config/template.json", manifest),
            ("content/src/A/A.csproj", project));

        return TemplatePackage.Read(nupkg);
    }

    private static void Zip(string path, params (string Entry, string Text)[] entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach ((string entry, string text) in entries)
        {
            using var writer = new StreamWriter(archive.CreateEntry(entry).Open());
            writer.Write(text);
        }
    }
}

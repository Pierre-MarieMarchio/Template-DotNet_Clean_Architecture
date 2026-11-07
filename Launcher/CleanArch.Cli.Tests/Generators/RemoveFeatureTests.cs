using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CleanArch.Cli.Generators.AddFeature;
using CleanArch.Cli.Generators.RemoveFeature;
using CleanArch.Cli.Generators.Shared;
using Xunit;
using static CleanArch.Cli.Generators.RemoveFeature.FeatureRemoval;

namespace CleanArch.Cli.Tests.Generators;

public sealed class RemoveFeatureTests
{
    [Fact]
    public void HandWritten() => Expectations.Hold(HandWrittenCases);

    [Fact]
    public void Report() => Expectations.Hold(ReportCases);

    [Fact]
    public void Undo() => Expectations.Hold(UndoCases);

    [Fact]
    public void Generator() => Expectations.Hold(GeneratorCases);

    [Fact]
    public void Migration() => Expectations.Hold(MigrationCases);

    [Fact]
    public void Order() => Expectations.Hold(OrderCases);

    [Fact]
    public void NameCollision() => Expectations.Hold(NameCollisionCases);

    [Fact]
    public void SharedFiles() => Expectations.Hold(SharedFilesCases);

    [Fact]
    public void Page() => Expectations.Hold(PageCases);

    [Theory]
    [InlineData("var a = \"x\\\"y\"; // Widgets", "var a = \"\"; ")]
    [InlineData("x = @\"a\"\"b\" + 'c';", "x = \"\" + ;")]
    [InlineData("y = \"\"\"raw \"Widgets\" \"\"\";", "y = \"\";")]
    [InlineData("z = $\"{w}\\\\\" + Widgets;", "z = \"\" + Widgets;")]
    public void Code_BlanksEveryLiteral_AndDropsTheTrailingComment(string line, string code) =>
        Assert.Equal(code, RemovalScan.Code(line));

    [Fact]
    public void WithoutAnApi() => Expectations.Hold(WithoutTheApiCases);

    private static (int Code, string Output, string Error) Invoke(string root, params string[] args) => Invoke(root, Recording([]), args);

    private static (int Code, string Output, string Error) Invoke(string root, Func<TextEdits.Command, int> run, params string[] args) =>
        GeneratorRun.Invoke(RemoveFeatureCommand.Build(GeneratorRun.Project(root), _ => new FakeExecutor(run)), args);

    private const string _removeMigration =
        "namespace Acme.Infrastructure.Persistence.Migrations\n{\n    public partial class {0} : Migration\n    {\n"
        + "        protected override void Up(MigrationBuilder migrationBuilder)\n        {\n            migrationBuilder.DropTable(\n                name: \"widgets\",\n                schema: \"widgets\");\n        }\n\n"
        + "        protected override void Down(MigrationBuilder migrationBuilder)\n        {\n            migrationBuilder.CreateTable(\n                name: \"widgets\",\n                schema: \"widgets\");\n        }\n    }\n}\n";

    private static Func<TextEdits.Command, int> Recording(List<TextEdits.Command> ran, string? failing = null, bool ensured = false) => command =>
    {
        ran.Add(command);

        if (command.Arguments is ["migrations", "add", { } name, ..] && name != "--check" && command.Display != failing)
        {

            string migration = _removeMigration.Replace("{0}", name, StringComparison.Ordinal);

            Put(
                command.WorkingDirectory,
                $"Src/Infrastructure/A.Infrastructure.Persistence/Migrations/20990101000000_{name}.cs",
                ensured ? migration.Replace("        {\n            migrationBuilder.CreateTable(", "        {\n            migrationBuilder.EnsureSchema(\n                name: \"widgets\");\n\n            migrationBuilder.CreateTable(", StringComparison.Ordinal) : migration);
        }

        return command.Display == failing ? 1 : 0;
    };

    private static string[] Launched(List<TextEdits.Command> ran) => [.. ran.Select(command => command.Display)];

    private static int _trees;

    private static string NewRoot(string scratch) => Path.Combine(scratch, (++_trees).ToString(CultureInfo.InvariantCulture));

    private static void Commit(string root) => FakeGit.Commit(root);

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

    private static string Skeleton(string scratch, string edits = "")
    {
        string root = NewRoot(scratch);

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
        expect(ran.All(command => command.Display.EndsWith("migrations add --check", StringComparison.Ordinal)), $"{name}: a refusal ran {string.Join(", ", Launched(ran))}.");

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
            Launched(ran).SequenceEqual(["dotnet cleanarch docs-index", "dotnet cleanarch build", "dotnet cleanarch openapi --no-build"]),
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

        FakeGit.Forget(loose);
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
        var waiting = Invoke(checkedTree, Recording(checks, "dotnet cleanarch migrations add --check"), "Widgets", "--apply");

        expect(waiting.Code == 1 && Same(pending, Snapshot(checkedTree)), "A model change waiting for a migration did not stop --apply before it changed anything.");
        expect(waiting.Error.Contains("migrations add --check` failed", StringComparison.Ordinal) && Launched(checks).SequenceEqual(["dotnet cleanarch migrations add --check"]), $"The refusal for a waiting model change does not say why, or ran more: {waiting.Error}");

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
            ("docs/reference/api.md", "Under `/api/v1/widgets`.", true),
            ("docs/reference/api.md", "| `widget.notFound` | 404 | Missing, or another caller's |", true),
            ("docs/reference/api.md", "Under `/api/v1/widgets-archive`, which Widgets feeds.", false),
            ("docs/guides/adding-a-feature.md", "Read [the Widgets page](../features/widgets.md#fields) first.", true),
            ("docs/guides/adding-a-feature.md", "Widgets, WidgetService and IWidgetStore are words in a sentence.", false),
            ("A.Api.http", "GET {{host}}/api/v1/widgets/{{widgetId}}", true),
            ("A.Api.http", "GET {{host}}/api/v1/widgets-archive", false),
            ("A.slnx", "<Folder Name=\"/Widgets/\" />", false),
            ("Directory.Build.props", "<Widgets>true</Widgets>", false),
            ("Directory.Build.targets", "<WidgetService />", false),
            ("Tools/Sample.cs", "var service = new WidgetService();", false),
            ("Src/Presentation/A.Presentation.Client/Generated/WidgetsClient.cs", "public sealed class WidgetsClient(IWidgetService widgets);", false),
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

    private static string Json(string[] lines) => new JsonArray([.. lines.Select(line => (JsonNode)JsonValue.Create(line))]).ToJsonString();

    private static string Edit(string path, string anchor, string position, string[] lines, string[]? replaced = null) =>
        $"{{ \"path\": \"{path}\", \"anchor\": {JsonValue.Create(anchor).ToJsonString()}, \"position\": \"{position}\", \"lines\": {Json(lines)}"
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

    private static int Generate(string root, params string[] args) =>
        GeneratorRun.Invoke(AddFeatureCommand.Build(GeneratorRun.Project(root), _ => new FakeExecutor(_ => 0), NewFeatureTests.Version), args).Code;

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
        Put(root, "docs/README.md", "# Documentation\n");
        Put(root, "docs/reference/api.md", "## Routes\n\n## Error codes\n\n### Resources\n\n| `code` | Status | When |\n|---|---|---|\n");
    }

    private const string _appDbContext =
        "    public const string PlatformSchema = \"platform\";\n"
        + "    internal DbSet<IdempotencyRecord> IdempotencyKeys => Set<IdempotencyRecord>();\n"
        + "        modelBuilder.ApplyConfiguration(new IdempotencyRecordConfiguration());\n";

    private const string _hostComposition =
        "        services.AddPurgeExpiredIdempotencyKeys();\n"
        + "        services.AddScoped<ICurrentAccessToken, ArchitectureTestCurrentAccessToken>();\n"
        + "internal sealed class ArchitectureTestBackgroundAuditActor : IAuditActor;\n";

    internal static string Tree(string scratch, Action<string>? ground = null)
    {
        string root = NewRoot(scratch);

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
                Launched(ran).SequenceEqual(["dotnet cleanarch migrations add --check", "dotnet cleanarch docs-index", "dotnet cleanarch build", "dotnet cleanarch openapi --no-build"]),
                $"Removing {features} did not check the model, then rewrite the index, build once and rewrite the OpenAPI reference from that build: {string.Join(" | ", Launched(ran))}");

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
            Launched(ran).SequenceEqual(["dotnet cleanarch migrations add --check", "dotnet cleanarch docs-index", "dotnet cleanarch build", "dotnet cleanarch migrations add RemoveWidgets --no-build", "dotnet cleanarch openapi --no-build"]),
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
        expect(Launched(second).Contains("dotnet cleanarch migrations add RemoveWidgets2 --no-build"), $"A second removal did not name its migration apart from the first: {string.Join(" | ", Launched(second))}");
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

        var stopped = Invoke(failed, Recording([], "dotnet cleanarch build"), "Widgets", "--apply");

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
        string root = NewRoot(scratch);

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
        var second = Invoke(two, "Sprockets", "--apply");

        expect(second.Code == 0, $"(b) the second owner feature was refused: {second.Output}{second.Error}");
        Commit(two);

        var first = Invoke(two, "Widgets", "--apply");

        expect(first.Code == 0, $"(b) the first owner feature was refused once it was the only one: {first.Output}{first.Error}");
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
        string template = Path.Combine(Repository.Root, "docs", "_page-templates", "feature.md");

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
        expect(!report.Output.Contains("docs/README.md:", StringComparison.Ordinal), $"The report lists an edit of docs/README.md, whose pages docs-index lists instead: {report.Output}");
        expect(!report.Output.Contains($"{pagePath}:", StringComparison.Ordinal), "The page's own lines were listed as matches.");

        var applied = Invoke(fresh, "Widgets", "--apply");

        expect(applied.Code == 0, $"A feature with its page was refused: {applied.Output}{applied.Error}");
        expect(Same(original, Snapshot(fresh)), $"After removing the feature, its page or the folder of pages was left behind, or the template went with it: {Differences(original, Snapshot(fresh))}");

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
                Launched(ran).SequenceEqual(["dotnet cleanarch migrations add --check", "dotnet cleanarch docs-index", "dotnet cleanarch build"]),
                $"Removing {features} without the API ran more than the check and the build: {string.Join(" | ", Launched(ran))}");
        }
    }
}

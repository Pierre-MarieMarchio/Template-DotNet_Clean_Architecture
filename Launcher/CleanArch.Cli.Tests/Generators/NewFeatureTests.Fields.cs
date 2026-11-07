using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CleanArch.Cli.Generators.AddField;
using CleanArch.Cli.Generators.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static CleanArch.Cli.Generators.Shared.FeatureCode;

namespace CleanArch.Cli.Tests.Generators;

public sealed partial class NewFeatureTests
{
    private const string _existingFields = "Title:string(120), Size:int>=1";

    private const string _everyAdded =
        "Nickname:string(40)?, Code:string(40)='none', Notes:text?, Summary:text(500)='', Count:int>=0<=1000=0, Total:long?>-5, "
        + "Budget:decimal(12,2)>=0=0, Ratio:decimal(5,4)?, Paid:bool=false, Due:DateOnly='2026-01-01', "
        + "StartsAt:DateTimeOffset='2026-01-01T02:00:00+02:00', Reference:Guid?, Batch:Guid='3f2504e0-4f89-41d3-9a0c-0305e82c3301'";

    private static readonly (string Label, string[] Options)[] _fieldShapes =
    [
        ("record, owner, delete", ["--access", "owner", "--deletion", "delete"]),
        ("record, shared, etag, get,list,update", ["--access", "shared", "--concurrency", "etag", "--operations", "get,list,update"]),
        ("aggregate, owner, anonymise", ["--access", "owner", "--deletion", "anonymise", "--model", "aggregate", "--concurrency", "etag"]),
        ("aggregate, shared, get,list,create", ["--access", "shared", "--model", "aggregate", "--concurrency", "etag", "--operations", "get,list,create"]),
    ];

    private const string _thingMigrations = "Src/Infrastructure/A.Infrastructure.Persistence/Migrations";

    private static (int Code, string Output, string Error) InvokeField(string root, Func<TextEdits.Command, int> run, params string[] args)
    {
        var (code, output, error) = GeneratorRun.Invoke(AddFieldCommand.Build(GeneratorRun.Project(root), _ => new FakeExecutor(run)), args);

        return (code, output.ReplaceLineEndings("\n"), error.ReplaceLineEndings("\n"));
    }

    private static Func<TextEdits.Command, int> Migrating(string root, List<TextEdits.Command> ran, string table) =>
        command =>
        {
            ran.Add(command);

            if (command.Arguments is ["migrations", "add", { } name, ..] && name != "--check")
            {
                var manifest = JsonNode.Parse(File.ReadAllText(Directory.EnumerateFiles(System.IO.Path.Combine(root, FeatureManifest.Folder)).Single()))!;
                var fields = FieldGrammar.Parse(
                    manifest["addedFields"]!.AsArray()[^1]!["declaration"]!.GetValue<string>(),
                    FieldProfile.AddedField,
                    [])!;

                Put(root, $"{_thingMigrations}/20260101000000_{name}.cs", SampleMigration(name, table, fields));
            }

            return 0;
        };

    private static string SampleMigration(string name, string table, IReadOnlyList<Field> fields)
    {
        string Column(Field field, int index) =>
            $$"""
                        migrationBuilder.AddColumn<{{FieldCode.Type(field).TrimEnd('?')}}>(
                            name: "{{field.Name}}",
                            schema: "things",
                            table: "{{table}}",
                            nullable: {{(field.Nullable ? "true" : "false")}}{{(field.Nullable || index % 2 == 1 ? ");" : $",\n                defaultValue: default({FieldCode.Type(field)}));")}}
            """;

        return $$"""
            using Microsoft.EntityFrameworkCore.Migrations;

            namespace Acme.Infrastructure.Persistence.Migrations
            {
                public partial class {{name}} : Migration
                {
                    protected override void Up(MigrationBuilder migrationBuilder)
                    {
            {{string.Join("\n\n", fields.Select(Column))}}
                    }

                    protected override void Down(MigrationBuilder migrationBuilder)
                    {
                    }
                }
            }

            """;
    }

    private static Dictionary<string, string> FeatureFiles(string root) =>
        Snapshot(root)
            .Where(pair => !pair.Key.StartsWith(FeatureManifest.Folder, StringComparison.Ordinal) && !pair.Key.StartsWith(_thingMigrations + "/2026", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static string Differing(Dictionary<string, string> expected, Dictionary<string, string> actual) =>
        string.Join(", ", expected.Keys.Union(actual.Keys).Where(path => !expected.TryGetValue(path, out string? want) || !actual.TryGetValue(path, out string? got) || want != got).Take(8));

    private static string Plain(string declaration) =>
        string.Join(", ", FieldGrammar.Parse(declaration, FieldProfile.AddedField, [])!.Select(field => field.Declaration));

    private static void FieldAdditionCases(string scratch, Action<bool, string> expect)
    {
        foreach (var (label, options) in _fieldShapes)
        {
            string widened = FieldTree(scratch);
            var expected = Invoke(widened, ["Things", "Thing", .. options, "--fields", $"{_existingFields}, {Plain(_everyAdded)}"]);

            string root = FieldTree(scratch);
            var created = Invoke(root, ["Things", "Thing", .. options, "--fields", _existingFields]);
            var ran = new List<TextEdits.Command>();
            var added = InvokeField(root, Migrating(root, ran, "Things"), "Things", _everyAdded);

            expect(expected.Code == 0 && created.Code == 0, $"[{label}] new-feature refused the fields new-field is compared with: {expected.Error}{created.Error}");
            expect(added.Code == 0, $"[{label}] new-field refused a field of every type: {added.Error}");
            expect(
                Same(FeatureFiles(widened), FeatureFiles(root)),
                $"[{label}] new-field did not leave each file as new-feature --fields writes it with the fields added: {Differing(FeatureFiles(widened), FeatureFiles(root))}");

            var want = JsonNode.Parse(File.ReadAllText(System.IO.Path.Combine(widened, ".feature-manifest/Things.json")))!;
            var got = JsonNode.Parse(File.ReadAllText(System.IO.Path.Combine(root, ".feature-manifest/Things.json")))!;

            expect(
                got["files"]!.ToJsonString() == want["files"]!.ToJsonString(),
                $"[{label}] the manifest does not record the SHA-256 of each file as it now stands, so remove-feature would read them as edited.");
            expect(
                string.Join(", ", got["fields"]!.AsArray().Select(field => field!["declaration"]!.GetValue<string>()))
                    == string.Join(", ", want["fields"]!.AsArray().Select(field => field!["declaration"]!.GetValue<string>())),
                $"[{label}] the manifest does not record the fields added after the others.");
            expect(
                got["fields"]!.AsArray().Single(field => field!["name"]!.GetValue<string>() == "Budget")!["default"]!.GetValue<string>() == "0"
                    && got["fields"]!.AsArray().Single(field => field!["name"]!.GetValue<string>() == "Notes")!["default"] is null,
                $"[{label}] the manifest does not record the default of each field that has one, and only those.");

            var addition = got["addedFields"]!.AsArray().Single()!;

            expect(
                addition["declaration"]!.GetValue<string>() == string.Join(", ", FieldGrammar.Parse(_everyAdded, FieldProfile.AddedField, [])!.Select(field => field.Declared))
                    && addition["migration"]!.GetValue<string>() == "AddNicknameAnd12MoreFieldsToThings"
                    && addition["files"]!.AsArray().Count > 0
                    && addition["files"]!.AsArray().All(file => file!["after"] is not null && file["changes"]!.AsArray().Count > 0),
                $"[{label}] addedFields does not record the declaration, the migration and each file's change with its SHA-256.");
            expect(
                ran.Select(command => command.Display).SequenceEqual(
                [
                    "dotnet cleanarch migrations add --check",
                    "dotnet cleanarch build",
                    "dotnet cleanarch migrations add AddNicknameAnd12MoreFieldsToThings --no-build",
                    "dotnet cleanarch openapi --no-build",
                ]),
                $"[{label}] new-field did not check, build once, add the migration and rewrite the OpenAPI reference, in that order: {string.Join(" | ", ran.Select(command => command.Display))}");

            string migration = File.ReadAllText(Directory.EnumerateFiles(System.IO.Path.Combine(root, _thingMigrations), "*_AddNicknameAnd12MoreFieldsToThings.cs").Single());

            foreach (string value in new[]
            {
                "defaultValue: \"none\"", "defaultValue: \"\"", "defaultValue: 0)", "defaultValue: 0m", "defaultValue: false", "defaultValue: new DateOnly(2026, 1, 1)",
                "defaultValue: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)", "defaultValue: new Guid(\"3f2504e0-4f89-41d3-9a0c-0305e82c3301\")",
            })
            {
                expect(migration.Contains(value, StringComparison.Ordinal), $"[{label}] the migration does not set '{value}' for the rows the table holds:\n{migration}");
            }

            expect(
                !migration.Contains("default(", StringComparison.Ordinal) && Regex.Count(migration, "defaultValue:") == 8,
                $"[{label}] the migration keeps a default dotnet ef wrote, or gives one to a nullable column:\n{migration}");
            expect(
                added.Output.Contains("Breaking change: Code is required", StringComparison.Ordinal)
                    && added.Output.Contains("Notes is nullable: a request may leave it out, so none breaks", StringComparison.Ordinal),
                $"[{label}] the report does not say which field breaks the clients and which does not: {added.Output}");

            ran = [];
            var again = InvokeField(root, Recording(ran), "Things", _everyAdded);

            expect(
                again.Code == 1 && again.Error.Contains("Things already has a field Nickname", StringComparison.Ordinal) && ran.Count == 0
                    && Same(FeatureFiles(widened), FeatureFiles(root)),
                $"[{label}] adding the same fields again was not refused before anything was written or run: {again.Error}");
        }
    }

    private static void FieldFormattingCases(string scratch, Action<bool, string> expect)
    {
        string[] options = ["--access", "owner", "--deletion", "anonymise", "--model", "aggregate", "--concurrency", "etag"];
        const string fields = "Budget:decimal(12,2)>=0=5, Due:DateOnly?";
        string widened = FieldTree(scratch);

        Invoke(widened, ["Things", "Thing", .. options, "--fields", $"{_existingFields}, {Plain(fields)}"]);

        string root = FieldTree(scratch);

        Invoke(root, ["Things", "Thing", .. options, "--fields", _existingFields]);

        string[] featureFiles = [.. Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => path.Contains($"{System.IO.Path.DirectorySeparatorChar}Things{System.IO.Path.DirectorySeparatorChar}", StringComparison.Ordinal))];

        foreach (string file in featureFiles)
        {
            string reformatted = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot().NormalizeWhitespace("\t", "\n").ToFullString();

            File.WriteAllText(file, reformatted + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }

        const string aggregate = "Src/Domain/A.Domain/Features/Things/Model/Entities/Thing.cs";
        const string own = "public bool IsTitled(string title) => Title == title;";
        string aggregateFile = System.IO.Path.Combine(root, aggregate);

        File.WriteAllText(aggregateFile, File.ReadAllText(aggregateFile).Replace("public void Anonymise()", own + "\n\tpublic void Anonymise()", StringComparison.Ordinal));

        var added = InvokeField(root, Migrating(root, [], "Things"), "Things", fields);

        expect(added.Code == 0, $"new-field refused a feature whose files were reformatted: {added.Error}");

        foreach (string file in featureFiles)
        {
            string relative = System.IO.Path.GetRelativePath(root, file).Replace('\\', '/');
            string got = File.ReadAllText(file);
            string want = File.ReadAllText(System.IO.Path.Combine(widened, relative));

            if (relative == aggregate)
            {
                expect(got.Contains(own, StringComparison.Ordinal), "new-field dropped a member a person added to the aggregate.");
                got = got.Replace(own, string.Empty, StringComparison.Ordinal);
            }

            expect(
                SyntaxMerge.Normalized(CSharpSyntaxTree.ParseText(got).GetRoot()) == SyntaxMerge.Normalized(CSharpSyntaxTree.ParseText(want).GetRoot()),
                $"{relative}: once reformatted, new-field did not give it the members new-feature --fields writes with the field.");
        }

        expect(
            File.ReadAllText(aggregateFile).Contains("\n\tprivate static string RequireTitle(string value)", StringComparison.Ordinal),
            "new-field rewrote the formatting of a member the field does not change.");
    }

    private static void FieldRefusalCases(string scratch, Action<bool, string> expect)
    {
        string root = FieldTree(scratch);

        Invoke(root, "Things", "Thing", "--access", "owner", "--deletion", "anonymise", "--model", "aggregate", "--concurrency", "etag", "--fields", _existingFields);
        Invoke(root, "Plains", "Plain", "--access", "shared");
        Directory.CreateDirectory(System.IO.Path.Combine(root, "Src/Application/A.Application/Features/Notes"));

        (string[] Args, string Fragment)[] refusals =
        [
            (["Notes", "Budget:decimal=0"], "it was written by hand, as the project's example features (TodoLists, Notes and the others) are"),
            (["Gizmos", "Budget:decimal=0"], "No feature called Gizmos"),
            (["Plains", "Budget:decimal=0"], "Plains was generated without --fields"),
            (["things", "Budget:decimal=0"], "is not a feature's folder"),
            (["Things", "Budget:decimal"], "Budget cannot be null, and the rows the table already holds need a value for it: give one its rules accept after '=', as Budget:decimal(18,2)=0"),
            (["Things", "Due:DateOnly"], "as Due:DateOnly='2026-01-01'"),
            (["Things", "Notes:text?=''"], "Notes is nullable, so the rows the table already holds start with null"),
            (["Things", "Count:int>=1=0"], "the default of Count, 0, breaks the rule the field checks: it must be at least 1"),
            (["Things", "Count:int<10=10"], "it must be less than 10"),
            (["Things", "Count:int=1.5"], "is not a whole number an int holds"),
            (["Things", "Count:long=x"], "expected a number after '=', the default of Count"),
            (["Things", "Budget:decimal(5,2)=1000"], "does not fit decimal(5,2)"),
            (["Things", "Paid:bool=1"], "expected true or false after '='"),
            (["Things", "Code:string(10)=' '"], "is blank, which the field refuses"),
            (["Things", "Code:string(10)=' x'"], "starts or ends with a space"),
            (["Things", "Code:string(3)='abcd'"], "is 4 characters long, and the field holds 3 at most"),
            (["Things", "Body:text(3)='abcd'"], "is 4 characters long, and the field holds 3 at most"),
            (["Things", "Code:string(10)=none"], "expected a value between quotes after '='"),
            (["Things", "Code:string(10)='none"], "a quote opens the default of Code and none closes it"),
            (["Things", "Due:DateOnly='2026-13-01'"], "is not a date written yyyy-MM-dd"),
            (["Things", "Due:DateOnly='0001-01-01'"], "is the empty value, which the field refuses"),
            (["Things", "At:DateTimeOffset='2026-01-01T00:00:00'"], "is not an instant written yyyy-MM-ddTHH:mm:ss with its offset"),
            (["Things", "Batch:Guid='00000000-0000-0000-0000-000000000000'"], "is the empty value"),
            (["Things", "Batch:Guid='x'"], "is not a Guid written with its hyphens"),
            (["Things", "Title:string(10)='x'"], "Things already has a field Title (Title:string(120))"),
            (["Things", "TITLE:string(10)='x'"], "Things already has a field Title"),
            (["Things", "TitleMaxLength:int=0"], "is the name the generated code gives a constant, a method or a local of Title"),
            (["Things", "Code:string(16)='x'"], "Give it string(17)"),
            (["Things", "Code:string(10)='x' Count:int=0"], "The fields to add, at character 21: expected a comma"),
        ];

        var before = Snapshot(root);

        foreach (var (args, fragment) in refusals)
        {
            var ran = new List<TextEdits.Command>();
            var result = InvokeField(root, Recording(ran), args);
            string label = string.Join(' ', args);

            expect(
                result.Code == 1 && result.Error.Contains(fragment, StringComparison.Ordinal) && result.Error.Contains("Nothing was written.", StringComparison.Ordinal),
                $"[new-field {label}] was not refused with '{fragment}': {result.Error.Split('\n')[0]}");
            expect(Same(before, Snapshot(root)) && ran.Count == 0, $"[new-field {label}] wrote or ran something.");
        }

        var onFeature = Invoke(root, "Widgets", "Widget", "--access", "shared", "--fields", "Code:string(10)='x'");

        expect(
            onFeature.Code == 1 && onFeature.Error.Contains("is what add field gives the rows a table already holds", StringComparison.Ordinal),
            $"new-feature --fields took a default, which only new-field gives: {onFeature.Error.Split('\n')[0]}");
    }

    private static void ManifestIdentityCases(string scratch, Action<bool, string> expect)
    {
        string root = FieldTree(scratch);

        Invoke(root, "Things", "Thing", "--access", "shared", "--fields", _existingFields);

        string path = System.IO.Path.Combine(root, FeatureManifest.PathOf("Things"));
        var manifest = JsonNode.Parse(File.ReadAllText(path))!.AsObject();

        expect(
            manifest["generator"]?.GetValue<string>() == Generator && manifest["generatorVersion"]?.GetValue<string>() == Version,
            $"The manifest does not name the tool and the version that wrote it: {manifest.ToJsonString()}");

        manifest["generator"] = ScriptGenerator;
        manifest.Remove("generatorVersion");
        File.WriteAllText(path, manifest.ToJsonString());

        var fromTheScript = InvokeField(root, Migrating(root, [], "Things"), "Things", "Budget:decimal(12,2)>=0=0");

        expect(fromTheScript.Code == 0, $"A manifest {ScriptGenerator} wrote, before the tool, was refused: {fromTheScript.Error}");

        string other = FieldTree(scratch);

        Invoke(other, "Things", "Thing", "--access", "shared", "--fields", _existingFields);

        string otherPath = System.IO.Path.Combine(other, FeatureManifest.PathOf("Things"));
        var foreign = JsonNode.Parse(File.ReadAllText(otherPath))!.AsObject();

        foreign["generator"] = "Tools/Other.cs";
        File.WriteAllText(otherPath, foreign.ToJsonString());

        var refused = InvokeField(other, Recording([]), "Things", "Budget:decimal(12,2)>=0=0");

        expect(
            refused.Code == 1 && refused.Error.Contains("is not a manifest of schema version", StringComparison.Ordinal),
            $"A manifest another generator wrote was not refused: {refused.Error}");
    }

    private static void FieldHandEditCases(string scratch, Action<bool, string> expect)
    {
        const string aggregate = "Src/Domain/A.Domain/Features/Things/Model/Entities/Thing.cs";
        const string record = "Src/Infrastructure/A.Infrastructure.Persistence/Features/Things/Rows/Models/ThingRecord.cs";
        const string validator = "Src/Application/A.Application/Features/Things/UseCases/Commands/UpdateThing/UpdateThingCommandValidator.cs";

        (string Label, Action<string> Edit, string Fragment)[] edits =
        [
            ("a statement added to a member the field changes", root => Rewrite(root, aggregate, "        Title = validTitle;", "        Title = validTitle;\n        Size = validSize;"),
                "Thing.Update(string, int) differs from what add feature wrote, so add field leaves it to you. Make this change in it by hand:"),
            ("a member the field changes, removed", root => Rewrite(root, validator, "    public UpdateThingCommandValidator()", "    private void Unused()"),
                "the constructor of UpdateThingCommandValidator, which add feature wrote, is not in the file"),
            ("a member the field declares, declared already", root => Rewrite(root, record, "    public int Size { get; set; }", "    public int Size { get; set; }\n\n    public decimal Budget { get; set; }"),
                "ThingRecord.Budget is declared already, and these fields declare it"),
            ("a file the field changes, deleted", root => File.Delete(System.IO.Path.Combine(root, record)), $"{record}, which add feature wrote and .feature-manifest/Things.json records, is gone"),
            ("a file that does not parse", root => File.AppendAllText(System.IO.Path.Combine(root, record), "}\n"), $"{record} does not parse, at line"),
        ];

        foreach (var (label, edit, fragment) in edits)
        {
            string root = FieldTree(scratch);

            Invoke(root, "Things", "Thing", "--access", "owner", "--deletion", "delete", "--model", "aggregate", "--concurrency", "etag", "--fields", _existingFields);
            edit(root);

            var before = Snapshot(root);
            var ran = new List<TextEdits.Command>();
            var result = InvokeField(root, Recording(ran), "Things", "Budget:decimal(12,2)>=0=0");

            expect(
                result.Code == 1 && result.Error.Contains(fragment, StringComparison.Ordinal) && result.Error.Contains("Nothing was written", StringComparison.Ordinal),
                $"[{label}] new-field was not refused with '{fragment}': {result.Error}");
            expect(Same(before, Snapshot(root)) && ran.Count == 0, $"[{label}] new-field wrote some of its changes, or ran something, before refusing.");
        }

        string hinted = FieldTree(scratch);

        Invoke(hinted, "Things", "Thing", "--access", "owner", "--deletion", "delete", "--model", "aggregate", "--concurrency", "etag", "--fields", _existingFields);
        Rewrite(hinted, aggregate, "        Title = validTitle;", "        Title = validTitle;\n        Size = validSize;");

        var hint = InvokeField(hinted, Recording([]), "Things", "Budget:decimal(12,2)>=0=0");

        expect(
            hint.Error.Contains("  + " + "        decimal validBudget = RequireBudget(budget);", StringComparison.Ordinal)
                && hint.Error.Contains("  - " + "    public void Update(string title, int size)", StringComparison.Ordinal),
            $"The refusal of a member changed by hand does not show the lines to add and to remove: {hint.Error}");

        static void Rewrite(string root, string path, string from, string to)
        {
            string full = System.IO.Path.Combine(root, path);

            File.WriteAllText(full, File.ReadAllText(full).Replace(from, to, StringComparison.Ordinal), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
    }

    private static void FieldResumeCases(string scratch, Action<bool, string> expect)
    {
        string root = FieldTree(scratch);

        Invoke(root, "Things", "Thing", "--access", "shared", "--fields", _existingFields);

        var dryRan = new List<TextEdits.Command>();
        var before = Snapshot(root);
        var dry = InvokeField(root, Recording(dryRan), "Things", "Budget:decimal(12,2)>=0=5", "--dry-run");

        expect(dry.Code == 0 && dryRan.Count == 0 && Same(before, Snapshot(root)), $"new-field --dry-run wrote or ran something: {dry.Error}");
        expect(
            dry.Output.Contains("Would add Budget:decimal(12,2)>=0=5", StringComparison.Ordinal)
                && dry.Output.Contains("add field sets the defaultValue of Budget (5m) in the AddColumn of the AddBudgetToThings migration", StringComparison.Ordinal),
            $"new-field --dry-run does not print what it would change and run: {dry.Output}");

        var stopped = InvokeField(root, Recording([]), "Things", "Budget:decimal(12,2)>=0=5");

        expect(
            stopped.Code == 1 && stopped.Error.Contains("no migration named AddBudgetToThings", StringComparison.Ordinal)
                && stopped.Error.Contains("the same add field command resumes these steps", StringComparison.Ordinal),
            $"A migration that was not added did not stop the run with the way to resume it: {stopped.Error}");

        var written = FeatureFiles(root);
        string manifest = File.ReadAllText(System.IO.Path.Combine(root, ".feature-manifest/Things.json"));
        var ran = new List<TextEdits.Command>();
        var resumed = InvokeField(root, Migrating(root, ran, "Things"), "Things", "Budget:decimal(12,2)>=0=5");

        expect(
            resumed.Code == 0 && resumed.Output.Contains("so nothing is written again", StringComparison.Ordinal)
                && ran.Any(command => command.Display.Contains("migrations add AddBudgetToThings", StringComparison.Ordinal)),
            $"The same new-field after a failed step did not resume its steps: {resumed.Error}");
        expect(
            Same(written, FeatureFiles(root)) && manifest == File.ReadAllText(System.IO.Path.Combine(root, ".feature-manifest/Things.json")),
            "Resuming new-field wrote its changes or its manifest a second time.");
        expect(
            Directory.EnumerateFiles(System.IO.Path.Combine(root, _thingMigrations), "*_AddBudgetToThings.cs").Select(File.ReadAllText).Single().Contains("defaultValue: 5m", StringComparison.Ordinal),
            "Resuming new-field did not set the default in the migration it added.");

        ran = [];
        var done = InvokeField(root, Recording(ran), "Things", "Budget:decimal(12,2)>=0=5");

        expect(done.Code == 1 && done.Error.Contains("already has a field Budget", StringComparison.Ordinal) && ran.Count == 0, "A field added and migrated was not refused the second time.");

        string pending = FieldTree(scratch);

        Invoke(pending, "Things", "Thing", "--access", "shared", "--fields", _existingFields);

        var untouched = Snapshot(pending);
        var check = InvokeField(pending, Recording([], failing: "dotnet cleanarch migrations add --check"), "Things", "Budget:decimal(12,2)>=0=5");

        expect(
            check.Code == 1 && Same(untouched, Snapshot(pending)) && check.Error.Contains("dotnet cleanarch migrations add <Name>", StringComparison.Ordinal),
            "A model change no migration holds did not stop new-field before it wrote.");

        Directory.Delete(System.IO.Path.Combine(pending, _thingMigrations), recursive: true);

        var bare = Snapshot(pending);
        ran = [];
        var unmigrated = InvokeField(pending, Recording(ran), "Things", "Budget:decimal(12,2)>=0=5");

        expect(
            unmigrated.Code == 1 && Same(bare, Snapshot(pending)) && ran.Count == 0 && unmigrated.Error.Contains("has no initial migration", StringComparison.Ordinal),
            "A context without its initial migration did not stop new-field before it wrote or ran.");

        string failing = FieldTree(scratch);

        Invoke(failing, "Things", "Thing", "--access", "shared", "--fields", _existingFields);

        var rolled = Snapshot(failing);
        string record = System.IO.Path.Combine(failing, "Src/Infrastructure/A.Infrastructure.Persistence/Features/Things/Rows/Models/ThingRecord.cs");
        byte[] recorded = File.ReadAllBytes(record);

        int Blocked(TextEdits.Command command)
        {
            if (command.Display.EndsWith("migrations add --check", StringComparison.Ordinal))
            {
                File.Delete(record);
                Directory.CreateDirectory(record);
            }

            return 0;
        }

        var refused = InvokeField(failing, Blocked, "Things", "Budget:decimal(12,2)>=0=5");

        expect(
            refused.Code == 1 && refused.Error.Contains("nothing was left behind", StringComparison.Ordinal),
            $"A write that failed half way was not reported as rolled back: {refused.Error}");

        Directory.Delete(record);
        File.WriteAllBytes(record, recorded);
        expect(Same(rolled, Snapshot(failing)), "A new-field whose write failed half way left some of its changes behind.");
    }

    private static void FieldShapeCases(string scratch, Action<bool, string> expect)
    {
        string strings = FieldTree(scratch);
        string numbered = FieldTree(scratch);

        Invoke(strings, "Things", "Thing", "--access", "shared", "--fields", "Title:string(120)");
        Invoke(numbered, "Things", "Thing", "--access", "shared", "--fields", "Title:string(120), Size:int>=0");

        var sized = InvokeField(strings, Migrating(strings, [], "Things"), "Things", "Size:int>=0=0");

        expect(
            sized.Code == 0 && Same(FeatureFiles(numbered), FeatureFiles(strings))
                && File.ReadAllText(System.IO.Path.Combine(strings, "Src/Presentation/A.Api/Features/Things/Contracts/Requests/CreateThingRequest.cs"))
                    .Contains("using System.Text.Json.Serialization;", StringComparison.Ordinal),
            $"The first field of a value type did not bring the using its JsonRequired needs, as new-feature --fields writes it: {Differing(FeatureFiles(numbered), FeatureFiles(strings))}{sized.Error}");

        string readOnly = FieldTree(scratch);

        Invoke(readOnly, "Things", "Thing", "--access", "shared", "--operations", "get,list", "--fields", _existingFields);

        var read = InvokeField(readOnly, Migrating(readOnly, [], "Things"), "Things", "Code:string(10)='x'");

        expect(read.Code == 0 && read.Output.Contains("Code: only read, so it adds a member to what the feature returns and breaks no caller", StringComparison.Ordinal), $"A field of a feature that only reads was said to break a caller: {read.Output}{read.Error}");

        string noApi = WithoutTheApi(FieldTree(scratch), "A.Worker");

        Invoke(noApi, "Things", "Thing", "--access", "owner", "--deletion", "delete", "--fields", _existingFields);

        var ran = new List<TextEdits.Command>();
        var hosted = InvokeField(noApi, Migrating(noApi, ran, "Things"), "Things", "Budget:decimal(12,2)>=0=0, Due:DateOnly?");

        expect(hosted.Code == 0, $"new-field refused a feature of a project without the API: {hosted.Error}");
        expect(
            hosted.Output.Contains("every caller of the create operation and the update operation passes it", StringComparison.Ordinal)
                && !hosted.Output.Contains("POST ", StringComparison.Ordinal)
                && !ran.Any(command => command.Display.Contains("openapi", StringComparison.Ordinal)),
            $"Without the API, new-field spoke of routes or rewrote an OpenAPI reference: {hosted.Output}");

        string sqlite = NewTree(scratch, database: "sqlite");

        Put(sqlite, "Tests/Integration/A.Api.IntegrationTests/Infrastructure/TestDatabase.cs", "        [\n            AppDbContext.PlatformSchema,\n        ];\n");
        Invoke(sqlite, "Things", "Thing", "--access", "shared", "--fields", _existingFields);

        var prefixed = InvokeField(sqlite, Migrating(sqlite, [], "things_Things"), "Things", "Budget:decimal(12,2)>=0=7");

        expect(
            prefixed.Code == 0
                && Directory.EnumerateFiles(System.IO.Path.Combine(sqlite, _thingMigrations), "*_AddBudgetToThings.cs").Select(File.ReadAllText).Single().Contains("defaultValue: 7m", StringComparison.Ordinal),
            $"On SQLite, whose table name carries its schema, the default was not set: {prefixed.Error}");

        const string written =
            """
            protected override void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AddColumn<int>(
                    name: "Count",
                    table: "Things",
                    nullable: false);

                migrationBuilder.AddColumn<int>(
                    name: "Count",
                    table: "Others",
                    nullable: false,
                    defaultValue: 0);
            }
            """;
        var count = FieldGrammar.Parse("Count:int>=1=3", FieldProfile.AddedField, [])!;
        var inserted = MigrationDefaults.Apply($"class M {{ {written} }}", "Things", count);
        var twice = MigrationDefaults.Apply(inserted.Text!, "Things", count);
        var other = MigrationDefaults.Apply(inserted.Text!, "Gizmos", count);

        expect(
            inserted is { Changed: true, Problem: null } && inserted.Text!.Contains("nullable: false,\n        defaultValue: 3)", StringComparison.Ordinal)
                && inserted.Text.Contains("table: \"Others\",\n        nullable: false,\n        defaultValue: 0)", StringComparison.Ordinal),
            $"A default dotnet ef did not write was not added to the AddColumn of its table alone: {inserted.Text}");
        expect(twice is { Changed: false, Problem: null }, "Setting a default already set changed the migration again.");
        expect(other.Problem is { } missing && missing.Contains("0 AddColumn of Count to Gizmos", StringComparison.Ordinal), "A migration without the field's column was not refused.");

        var problems = new List<string>();
        var quoted = FieldGrammar.Parse(
            "Budget:decimal(12,2)>=0=0.50, Code:string(10)='it''s', At:DateTimeOffset='2026-01-01T10:00:00+02:00', Total:long<0=-3",
            FieldProfile.AddedField,
            problems);

        expect(
            quoted is not null
                && string.Join(", ", quoted.Select(field => field.Declared)) == "Budget:decimal(12,2)>=0=0.50, Code:string(10)='it''s', At:DateTimeOffset='2026-01-01T08:00:00Z', Total:long<0=-3"
                && string.Join(" | ", quoted.Select(field => field.Default!.Expression)) == "0.50m | \"it's\" | new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero) | -3L",
            $"The defaults were not read as written, nor carried to C# as the migration needs them: {string.Join(" | ", problems)}{(quoted is null ? string.Empty : string.Join(", ", quoted.Select(field => field.Declared)))}");
    }
}

using System.Text;
using System.Text.Json.Nodes;
using CleanArch.Cli.Generators.Shared;
using Microsoft.CodeAnalysis;

namespace CleanArch.Cli.Generators.AddFeature;

internal static partial class NewFeature
{
    private const string _fieldUsage =
        "Usage: dotnet cleanarch add field <Features> \"<Name:type>=<default>, ...\" [--dry-run]\n"
        + "  <Features>     a feature new-feature generated with --fields, which its manifest records\n"
        + "  <fields>       the fields to add, in the grammar of --fields. A field that cannot be null takes a\n"
        + "                 default after '=', the value the rows the table already holds receive, which its rules\n"
        + "                 must accept: a number, true or false, or a value between quotes for a string, a text, a\n"
        + "                 DateOnly, a DateTimeOffset or a Guid: \"Budget:decimal(12,2)>=0=0, Code:string(20)='none', Notes:text?\".\n"
        + "  --dry-run      print what changes, and write and run nothing.\n"
        + "  It adds each field to every member --fields writes it in, found through each file's syntax tree whatever its\n"
        + "  formatting; a member changed by hand since new-feature wrote it, or missing, writes nothing and prints the\n"
        + "  change to make by hand. It refuses while a context has no initial migration or the business model has a\n"
        + "  change no migration holds, then builds, adds the Add<Field>To<Features> migration with each default, and\n"
        + "  rewrites the OpenAPI reference and the clients written from it.";

    private const string _inProcess = "";

    private sealed record FieldRequest(string? Features, string? Declaration, string Root, bool DryRun);

    private sealed record Addition(Spec Before, Spec After, IReadOnlyList<Field> Added, string Migration, FeatureManifest Manifest, bool Pending);

    private sealed record FieldChange(string Path, byte[]? Original, byte[] Bytes, List<string> Members);

    internal static int RunField(string[] args, TextWriter output, TextWriter error, Func<TextEdits.Command, int> run)
    {
        var problems = new List<string>();
        var request = ParseFieldRequest(args, problems);
        string root = System.IO.Path.GetFullPath(request.Root);
        var addition = problems.Count == 0 ? CheckField(request, root, problems) : null;

        if (addition is null)
        {
            return RefuseField(error, problems);
        }

        if (addition.Pending)
        {
            output.WriteLine(
                $"{ManifestPath(addition.After)} records {string.Join(", ", addition.Added.Select(field => field.Declared))} already, "
                + $"and its migration {addition.Migration} is not complete: its files are written, so nothing is written again.");

            return request.DryRun ? 0 : FinishField(root, addition, output, error, run);
        }

        var changes = FieldChanges(root, addition, problems);

        if (problems.Count > 0)
        {
            ReportField(output, root, addition, changes, Mode.Refused);

            foreach (string problem in problems)
            {
                error.WriteLine($"error: {problem}");
            }

            error.WriteLine("Nothing was written: every change is made or none is. Make the changes above by hand, or put back what new-feature wrote.");

            return 1;
        }

        var manifest = UpdatedManifest(addition, changes);

        if (request.DryRun)
        {
            ReportField(output, root, addition, changes, Mode.DryRun);
            output.WriteLine();
            output.WriteLine("--dry-run: nothing was written and nothing was run.");

            return 0;
        }

        if (Preconditions(root, addition.After, output, run) is { } refusal)
        {
            error.WriteLine($"error: {refusal}");
            error.WriteLine("Nothing was written.");

            return 1;
        }

        try
        {
            TextEdits.Commit(
                root,
                [.. changes.Where(change => change.Original is null).Select(change => (change.Path, change.Bytes))],
                [
                    .. changes.Where(change => change.Original is not null).Select(change => (change.Path, change.Original!, change.Bytes)),
                    (ManifestPath(addition.After), File.ReadAllBytes(System.IO.Path.Combine(root, ManifestPath(addition.After))), manifest),
                ]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"error: nothing was left behind, but the write failed: {exception.Message}");

            return 1;
        }

        ReportField(output, root, addition, changes, Mode.Applied);

        return FinishField(root, addition, output, error, run);
    }

    private static int RefuseField(TextWriter error, List<string> problems)
    {
        foreach (string problem in problems)
        {
            error.WriteLine($"error: {problem}");
        }

        error.WriteLine();
        error.WriteLine(_fieldUsage);
        error.WriteLine();
        error.WriteLine("Nothing was written.");

        return 1;
    }

    private static FieldRequest ParseFieldRequest(string[] args, List<string> problems)
    {
        var positional = new List<string>();
        string? root = null;
        bool dryRun = false;

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];

            if (argument == "--dry-run")
            {
                dryRun = true;
            }
            else if (argument == "--root" && index + 1 < args.Length && root is null)
            {
                root = args[++index];
            }
            else if (argument.StartsWith("--", StringComparison.Ordinal))
            {
                problems.Add($"'{argument}' is not an option of new-field, which takes --root <path> and --dry-run.");
            }
            else
            {
                positional.Add(argument);
            }
        }

        if (positional.Count != 2)
        {
            problems.Add(
                $"Expected the feature and its new fields, and found {positional.Count} argument(s): "
                + "new-field Projects \"Budget:decimal(12,2)>=0=0\".");
        }

        return new FieldRequest(
            positional.Count > 0 ? positional[0] : null,
            positional.Count > 1 ? positional[1] : null,
            root ?? ".",
            dryRun);
    }

    private static Addition? CheckField(FieldRequest request, string root, List<string> problems)
    {
        string features = request.Features ?? string.Empty;

        if (!CSharpNames.PascalCase.IsMatch(features))
        {
            problems.Add($"'{features}' is not a feature's folder: <Features> is PascalCase and plural, e.g. Projects.");

            return null;
        }

        string prefix;
        bool tablePrefixes;

        try
        {
            prefix = ProductPrefix(root);
            tablePrefixes = TablePrefixes(root);
        }
        catch (InvalidOperationException exception)
        {
            problems.Add(exception.Message);

            return null;
        }

        string manifestPath = FeatureManifest.PathOf(features);
        string manifestFile = System.IO.Path.Combine(root, manifestPath);

        if (!File.Exists(manifestFile))
        {
            problems.Add(WrittenByHand(root, features)
                ? $"{features} has no manifest, {manifestPath}: it was written by hand, as the project's example features (TodoLists, "
                    + "Notes and the others) are, so new-field cannot know which members a field reaches. Add the field by hand: "
                    + "docs/guides/adding-a-feature.md, under Adding a field, lists every place it goes."
                : $"No feature called {features}: {manifestPath} does not exist. new-field adds a field to a feature that "
                    + "new-feature --fields generated.");

            return null;
        }

        if (FeatureManifest.Read(root, features, out string? unread) is not { } manifest)
        {
            problems.Add($"{manifestPath} is not the JSON object new-feature writes: it {unread}");

            return null;
        }

        if (ManifestSpec(manifest, manifestPath, prefix, tablePrefixes, root, problems) is not { } before)
        {
            return null;
        }

        var added = FieldGrammar.Parse(request.Declaration ?? string.Empty, FieldProfile.AddedField, problems);

        for (int index = 0; index < problems.Count; index++)
        {
            problems[index] = problems[index].Replace("--fields", "The fields to add", StringComparison.Ordinal);
        }

        if (added is null)
        {
            return null;
        }

        var existing = before.Fields!;
        string migration = MigrationName(features, added);
        string declared = string.Join(", ", added.Select(field => field.Declared));

        if (added.Any(field => existing.Any(present => string.Equals(present.Name, field.Name, StringComparison.OrdinalIgnoreCase))))
        {
            bool recorded = manifest.AddedFields.Any(entry => entry.Declaration == declared && entry.Migration == migration);
            var after = before with { Fields = existing };

            if (recorded && !Migrated(root, after, migration, added))
            {
                return new Addition(before with { Fields = [.. existing.Where(field => !added.Any(one => one.Name == field.Name))] }, after, added, migration, manifest, Pending: true);
            }

            foreach (var field in added.Where(field => existing.Any(present => string.Equals(present.Name, field.Name, StringComparison.OrdinalIgnoreCase))))
            {
                var present = existing.First(other => string.Equals(other.Name, field.Name, StringComparison.OrdinalIgnoreCase));

                problems.Add(
                    $"{features} already has a field {present.Name} ({present.Declaration}), which {manifestPath} records: a field is added "
                    + "once. To change it, edit it by hand in each layer and add a migration.");
            }

            return null;
        }

        IReadOnlyList<Field> combined = [.. existing, .. added];
        var known = FieldGrammar.NameProblems(existing, FieldProfile.Feature).ToHashSet(StringComparer.Ordinal);

        problems.AddRange(FieldGrammar.NameProblems(combined, FieldProfile.Feature)
            .Where(problem => !known.Contains(problem))
            .Select(problem => problem.Replace("--fields", "The fields to add", StringComparison.Ordinal)));

        var widened = before with { Fields = combined };
        var plan = Plan(widened, root, out _);
        var names = plan.Select(emission => System.IO.Path.GetFileNameWithoutExtension(emission.Path)).ToList();
        var conflicts = FieldCode.Conflicts(existing, features, before.Type, names, before.Deletion, before.Access == "owner", before.Has("list")).ToHashSet(StringComparer.Ordinal);

        problems.AddRange(FieldCode.Conflicts(combined, features, before.Type, names, before.Deletion, before.Access == "owner", before.Has("list"))
            .Where(problem => !conflicts.Contains(problem)));

        if (Directory.Exists(System.IO.Path.Combine(root, PersistenceProject(before), "Migrations"))
            && Directory.EnumerateFiles(System.IO.Path.Combine(root, PersistenceProject(before), "Migrations"), $"*_{migration}.cs").Any())
        {
            problems.Add($"A migration named {migration} already exists, and the migration of these fields takes that name. Rename that one first.");
        }

        return problems.Count > 0 ? null : new Addition(before, widened, added, migration, manifest, Pending: false);
    }

    private static string MigrationName(string features, List<Field> added) =>
        added.Count <= 2
            ? $"Add{string.Join("And", added.Select(field => field.Name))}To{features}"
            : $"Add{added[0].Name}And{added.Count - 1}MoreFieldsTo{features}";

    private static readonly string[] _sourceLayers = ["Src/Domain", "Src/Application", "Src/Infrastructure", "Src/Presentation"];

    private static bool WrittenByHand(string root, string features) =>
        _sourceLayers
            .Select(layer => System.IO.Path.Combine(root, layer))
            .Where(Directory.Exists)
            .SelectMany(Directory.EnumerateDirectories)
            .Any(project => Directory.Exists(System.IO.Path.Combine(project, "Features", features)));

    private static Spec? ManifestSpec(FeatureManifest manifest, string path, string prefix, bool tablePrefixes, string root, List<string> problems)
    {
        try
        {
            if (manifest.SchemaVersion != FeatureManifest.EditsVersion || manifest.Generator is not (Generator or ScriptGenerator))
            {
                problems.Add(
                    $"{path} is not a manifest of schema version {FeatureManifest.EditsVersion} written by {Generator} or {ScriptGenerator}, the ones add field reads. "
                    + "Add the field by hand: docs/guides/adding-a-feature.md, under Adding a field, lists every place it goes.");

                return null;
            }

            var options = manifest.Options!;
            string features = manifest.Feature!;
            string type = manifest.Type!;
            string access = options["access"]!.GetValue<string>();
            string[] operations = [.. options["operations"]!.AsArray().Select(operation => operation!.GetValue<string>())];

            if (manifest.Fields is not { } recorded)
            {
                problems.Add(
                    $"{features} was generated without --fields: its Name and Description are placeholders a person replaces by hand, so "
                    + "what its files hold is not a shape new-field can read. Add the field by hand: docs/guides/adding-a-feature.md, "
                    + "under Adding a field, lists every place it goes.");

                return null;
            }

            var reading = new List<string>();
            var fields = FieldGrammar.Parse(
                string.Join(", ", recorded.Select(field => field!["declaration"]!.GetValue<string>())),
                FieldProfile.Feature,
                reading);

            if (fields is null)
            {
                problems.Add($"{path} records fields this grammar no longer reads: {string.Join(" ", reading)}");

                return null;
            }

            bool hasApi = Directory.Exists(System.IO.Path.Combine(root, "Src", "Presentation", FolderOf(prefix, "Api")));

            return new Spec(
                prefix,
                features,
                type,
                access,
                options["deletion"]?.GetValue<string>() ?? string.Empty,
                operations,
                options["model"]!.GetValue<string>(),
                options["concurrency"]!.GetValue<string>(),
                null,
                hasApi,
                fields,
                tablePrefixes);
        }
        catch (Exception exception) when (exception is NullReferenceException or InvalidOperationException or FormatException)
        {
            problems.Add($"{path} lacks what new-feature records, the options and the fields among them: {exception.Message}");

            return null;
        }
    }

    private static List<FieldChange> FieldChanges(string root, Addition addition, List<string> problems)
    {
        var before = Plan(addition.Before, root, out _).ToDictionary(emission => emission.Path, StringComparer.Ordinal);
        var after = Plan(addition.After, root, out _);
        var recorded = addition.Manifest.Files.Keys.ToHashSet(StringComparer.Ordinal);
        var changes = new List<FieldChange>();

        foreach (string path in before.Keys.Where(path => after.All(emission => emission.Path != path)))
        {
            problems.Add($"{path}: these fields would remove it, which new-field never does. Add the fields by hand.");
        }

        foreach (var emission in after)
        {
            string full = System.IO.Path.Combine(root, emission.Path);

            if (!before.TryGetValue(emission.Path, out var earlier))
            {
                if (File.Exists(full))
                {
                    problems.Add($"{emission.Path} exists, and these fields need it written by the generator: move it away, or add the fields by hand.");
                }
                else
                {
                    changes.Add(new FieldChange(emission.Path, null, Bytes(emission), ["written"]));
                }

                continue;
            }

            if (earlier.Content == emission.Content)
            {
                continue;
            }

            if (!File.Exists(full))
            {
                if (recorded.Contains(emission.Path))
                {
                    problems.Add($"{emission.Path}, which new-feature wrote and {ManifestPath(addition.After)} records, is gone, so the fields cannot reach it. Put it back.");
                }

                continue;
            }

            byte[] original = File.ReadAllBytes(full);
            var merged = SyntaxMerge.Merge(emission.Path, earlier.Content, emission.Content, original);

            if (merged.Problems.Count > 0)
            {
                problems.AddRange(merged.Problems);

                continue;
            }

            changes.Add(new FieldChange(emission.Path, original, merged.Bytes!, merged.Changes));
        }

        return changes;
    }

    private static byte[] UpdatedManifest(Addition addition, List<FieldChange> changes)
    {
        var manifest = addition.Manifest;
        var written = FieldsOf(addition.Added);

        for (int index = 0; index < addition.Added.Count; index++)
        {
            var entry = written[index]!.DeepClone().AsObject();

            if (addition.Added[index].Default is { } value)
            {
                entry["default"] = value.Literal;
            }

            manifest.Fields!.Add((JsonNode)entry);
        }

        foreach (var change in changes)
        {
            manifest.Record(change.Path, change.Bytes);
        }

        manifest.RecordAddition(
            string.Join(", ", addition.Added.Select(field => field.Declared)),
            addition.Migration,
            changes.Select(change => (change.Path, change.Original, change.Bytes, (IReadOnlyList<string>)change.Members)));

        return manifest.Bytes();
    }

    private static bool Migrated(string root, Spec spec, string migration, IReadOnlyList<Field> added)
    {
        string folder = System.IO.Path.Combine(root, PersistenceProject(spec), "Migrations");
        string? file = Directory.Exists(folder) ? Directory.EnumerateFiles(folder, $"*_{migration}.cs").FirstOrDefault() : null;

        return file is not null && MigrationDefaults.Apply(File.ReadAllText(file), TableOf(spec), added) is { Problem: null, Changed: false };
    }

    private static string TableOf(Spec spec) => spec.TablePrefixes ? $"{Snake(spec.Features)}_{spec.Features}" : spec.Features;

    private static List<TextEdits.Command> FieldSteps(string root, Addition addition)
    {
        string folder = System.IO.Path.Combine(root, PersistenceProject(addition.After), "Migrations");
        bool written = Directory.Exists(folder) && Directory.EnumerateFiles(folder, $"*_{addition.Migration}.cs").Any();
        var defaults = addition.Added.Where(field => field.Default is not null).ToList();

        return
        [
            .. written ? Array.Empty<TextEdits.Command>() : [TextEdits.Task(root, "build"), TextEdits.FromLastBuild(root, "migration-add", addition.Migration)],
            .. defaults.Count == 0
                ? Array.Empty<TextEdits.Command>()
                : [new TextEdits.Command(
                    $"new-field sets the defaultValue of {string.Join(", ", defaults.Select(field => $"{field.Name} ({field.Default!.Expression})"))} "
                        + $"in the AddColumn of the {addition.Migration} migration",
                    root,
                    _inProcess,
                    [])],
            .. addition.After.HasApi ? TextEdits.ContractSteps(root, FolderOf(addition.After.Prefix, "Presentation.Client"), built: !written) : [],
        ];
    }

    private static int FinishField(string root, Addition addition, TextWriter output, TextWriter error, Func<TextEdits.Command, int> run)
    {
        int Run(TextEdits.Command command)
        {
            if (command.FileName != _inProcess)
            {
                return run(command);
            }

            string folder = System.IO.Path.Combine(root, PersistenceProject(addition.After), "Migrations");
            string? file = Directory.Exists(folder) ? Directory.EnumerateFiles(folder, $"*_{addition.Migration}.cs").FirstOrDefault() : null;

            if (file is null)
            {
                error.WriteLine($"error: no migration named {addition.Migration} was found in {PersistenceProject(addition.After)}/Migrations.");

                return 1;
            }

            byte[] original = File.ReadAllBytes(file);
            bool bom = original.AsSpan().StartsWith(Encoding.UTF8.Preamble);
            string text = Encoding.UTF8.GetString(original.AsSpan(bom ? 3 : 0));
            var applied = MigrationDefaults.Apply(text, TableOf(addition.After), addition.Added);

            if (applied.Problem is { } problem)
            {
                error.WriteLine($"error: {problem}");
                error.WriteLine("Set each defaultValue by hand in the migration's Up, the value the rows the table already holds receive.");

                return 1;
            }

            if (applied.Changed)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(applied.Text!);

                TextEdits.Commit(root, [], [(System.IO.Path.GetRelativePath(root, file), original, bom ? [.. Encoding.UTF8.Preamble, .. bytes] : bytes)]);
            }

            output.WriteLine($"Set in {System.IO.Path.GetFileName(file)}: {string.Join(", ", addition.Added.Where(field => field.Default is not null).Select(field => $"{field.Name} = {field.Default!.Expression}"))}.");

            return 0;
        }

        string done = $"the fields in every layer and in {ManifestPath(addition.After)}";

        if (TextEdits.RunAll(FieldSteps(root, addition), Run, output, error, done))
        {
            return 0;
        }

        error.WriteLine("Once the failure is fixed, the same new-field command resumes these steps, and writes nothing again.");

        return 1;
    }

    private static void ReportField(TextWriter output, string root, Addition addition, List<FieldChange> changes, Mode mode)
    {
        var spec = addition.After;
        string verb = mode == Mode.Applied ? "Added" : "Would add";

        output.WriteLine(
            $"{verb} {string.Join(", ", addition.Added.Select(field => field.Declared))} to {spec.Features}/{spec.Type} "
            + $"(access {spec.Access}, model {spec.Model}, operations {string.Join(',', spec.Operations)}, concurrency {spec.Concurrency}"
            + (spec.Access == "owner" ? $", deletion {spec.Deletion}" : string.Empty) + "), "
            + $"{(mode == Mode.Applied ? "changing" : "changes")} {changes.Count} file(s), each through its syntax tree:");

        foreach (var change in changes)
        {
            output.WriteLine($"  {change.Path}");

            foreach (string member in change.Members)
            {
                output.WriteLine($"      {member}");
            }
        }

        output.WriteLine();
        output.WriteLine(
            $"Manifest: {ManifestPath(spec)}{(mode == Mode.Applied ? string.Empty : ", not written")}: the fields join its fields, with each "
            + "default, its files their new SHA-256, and addedFields the declaration, the migration and each file's change, "
            + "with its SHA-256 before and after.");

        output.WriteLine();
        output.WriteLine("Each field is checked by the validator and by the aggregate or the service, stored in a column of its length or");
        output.WriteLine("precision, and carried by the contracts and the generated tests, as --fields writes it.");

        foreach (var field in addition.Added)
        {
            output.WriteLine();
            output.WriteLine(Compatibility(spec, field));
        }

        if (spec.Has("list") && addition.Added.Where(FieldCode.IsSortable).Select(field => field.Name).ToList() is { Count: > 0 } sortable)
        {
            output.WriteLine();
            output.WriteLine($"The list now sorts by {string.Join(", ", sortable)} too, each with its index, which the migration creates.");
        }

        if (mode != Mode.Applied)
        {
            output.WriteLine();
            output.WriteLine(mode == Mode.DryRun
                ? "Then a run without --dry-run checks that no model change waits for a migration, writes, and runs in this order, stopping at the first that fails:"
                : "Then, once the changes are made:");

            foreach (var step in FieldSteps(root, addition))
            {
                output.WriteLine($"  {step.Display}");
            }
        }
    }

    private static string Compatibility(Spec spec, Field field)
    {
        string route = $"/api/v1/{Kebab(spec.Features)}";
        string item = $"{route}/{{{CSharpNames.Lower(spec.Type)}Id}}";
        string[] writes =
        [
            .. spec.Has("create") ? [spec.HasApi ? $"POST {route}" : "the create operation"] : Array.Empty<string>(),
            .. spec.Has("update") ? [spec.HasApi ? $"PUT {item}" : "the update operation"] : Array.Empty<string>(),
        ];

        if (writes.Length == 0)
        {
            return $"{field.Name}: only read, so it adds a member to what the feature returns and breaks no caller.";
        }

        if (!field.Nullable)
        {
            return spec.HasApi
                ? $"Breaking change: {field.Name} is required, so {string.Join(" and ", writes)} answer 400 to a request without it from now "
                    + "on, and every client that does not send it yet breaks. Release the clients with the API, or declare the field "
                    + $"nullable ({(field with { Nullable = true }).Declaration}), which breaks none. The rows already stored take {field.Default!.Literal}."
                : $"Breaking change: {field.Name} is required, so every caller of {string.Join(" and ", writes)} passes it from now on. "
                    + $"The rows already stored take {field.Default!.Literal}.";
        }

        return $"{field.Name} is nullable: a {(spec.HasApi ? "request" : "caller")} may leave it out, so none breaks; the rows already stored hold null"
            + (spec.Has("update")
                ? $". An update replaces the whole {Separated(spec.Type, ' ')}, so one that leaves {field.Name} out stores null: a client that does not know the field yet clears it."
                : ".");
    }
}

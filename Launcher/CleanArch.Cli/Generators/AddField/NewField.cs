using CleanArch.Cli.Generators.Shared;
using Microsoft.CodeAnalysis;
using static CleanArch.Cli.Generators.Shared.CSharpNames;
using static CleanArch.Cli.Generators.Shared.FeatureCode;
using static CleanArch.Cli.Generators.Shared.ProjectFolders;

namespace CleanArch.Cli.Generators.AddField;

internal static class NewField
{
    internal const string Usage =
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

    internal static int Run(AddFieldRequest request, GenerationContext context)
    {
        var problems = new List<string>();
        var addition = CheckField(request, context.Root, context.Project, problems);

        return addition is null
            ? Refusal.Write(context.Error, problems, Usage)
            : new FieldGeneration(addition, context).Generate();
    }

    private static Addition? CheckField(AddFieldRequest request, string root, LocatedProject project, List<string> problems)
    {
        string features = request.Features;

        if (!CSharpNames.PascalCase.IsMatch(features))
        {
            problems.Add($"'{features}' is not a feature's folder: <Features> is PascalCase and plural, e.g. Projects.");

            return null;
        }

        string prefix = project.File.Name;
        bool tablePrefixes;

        try
        {
            tablePrefixes = TablePrefixes(project);
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

        var added = FieldGrammar.Parse(request.Declarations, FieldProfile.AddedField, problems);

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

    private static FeatureSpec? ManifestSpec(FeatureManifest manifest, string path, string prefix, bool tablePrefixes, string root, List<string> problems)
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

            bool hasApi = Directory.Exists(Full(root, $"{Presentation}/{FolderOf(prefix, "Api")}"));

            return new FeatureSpec(
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

    private static bool Migrated(string root, FeatureSpec spec, string migration, IReadOnlyList<Field> added)
    {
        string folder = System.IO.Path.Combine(root, PersistenceProject(spec), "Migrations");
        string? file = Directory.Exists(folder) ? Directory.EnumerateFiles(folder, $"*_{migration}.cs").FirstOrDefault() : null;

        return file is not null && MigrationDefaults.Apply(File.ReadAllText(file), TableOf(spec), added) is { Problem: null, Changed: false };
    }

    internal static string TableOf(FeatureSpec spec) => spec.TablePrefixes ? $"{Snake(spec.Features)}_{spec.Features}" : spec.Features;
}

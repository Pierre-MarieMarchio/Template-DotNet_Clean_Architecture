using System.Text;
using System.Text.Json.Nodes;
using CleanArch.Cli.Generators.Shared;
using Microsoft.CodeAnalysis;
using static CleanArch.Cli.Generators.Shared.FeatureCode;

namespace CleanArch.Cli.Generators.AddField;

internal sealed class FieldGeneration(Addition addition, GenerationContext context) : Generation(context)
{
    private readonly List<string> _failures = [];

    private List<FieldChange> _changes = [];

    private byte[] _manifest = [];

    protected override string Usage => NewField.Usage;

    protected override bool StopsAtDryRun => true;

    protected override IReadOnlyList<string> EditFailures => _failures;

    protected override IReadOnlyList<(string Path, byte[] Bytes)> Created =>
        [.. _changes.Where(change => change.Original is null).Select(change => (change.Path, change.Bytes))];

    protected override IReadOnlyList<(string Path, byte[] Original, byte[] Bytes)> Replaced =>
        [
            .. _changes.Where(change => change.Original is not null).Select(change => (change.Path, change.Original!, change.Bytes)),
            (ManifestPath(addition.After), File.ReadAllBytes(System.IO.Path.Combine(Root, ManifestPath(addition.After))), _manifest),
        ];

    protected override int? Concluded()
    {
        if (!addition.Pending)
        {
            return null;
        }

        Output.WriteLine(
            $"{ManifestPath(addition.After)} records {string.Join(", ", addition.Added.Select(field => field.Declared))} already, "
            + $"and its migration {addition.Migration} is not complete: its files are written, so nothing is written again.");

        return Context.DryRun ? Succeeded : Finish();
    }

    protected override bool Draft(List<string> problems)
    {
        _changes = FieldChanges(Root, addition, _failures);

        if (_failures.Count == 0)
        {
            _manifest = UpdatedManifest(addition, _changes);
        }

        return true;
    }

    protected override int RefuseEdits(IReadOnlyList<string> failures) =>
        RefuseAfterReport(failures, "Nothing was written: every change is made or none is. Make the changes above by hand, or put back what new-feature wrote.");

    protected override string? Preconditions() => FeatureCode.Preconditions(Root, addition.After, Output, Context.Executor);

    protected override void Report(ReportMode mode) => FieldReport.Write(Output, Root, addition, _changes, mode);

    protected override int Finish() => FinishField(Root, addition, Output, Error, Context.Executor);

    private const string _inProcess = "";


    private static List<FieldChange> FieldChanges(string root, Addition addition, List<string> problems)
    {
        var before = Plan(addition.Before, root, out _).ToDictionary(emission => emission.Path, StringComparer.Ordinal);
        var after = Plan(addition.After, root, out _);
        var recorded = addition.Manifest.Files.Keys.ToHashSet(StringComparer.Ordinal);

        foreach (string path in before.Keys.Where(path => after.All(emission => emission.Path != path)))
        {
            problems.Add($"{path}: these fields would remove it, which new-field never does. Add the fields by hand.");
        }

        return [.. after.Select(emission => Change(root, addition, before.GetValueOrDefault(emission.Path), recorded, emission, problems)).OfType<FieldChange>()];
    }

    private static FieldChange? Change(string root, Addition addition, Emission? earlier, HashSet<string> recorded, Emission emission, List<string> problems)
    {
        string full = System.IO.Path.Combine(root, emission.Path);

        if (earlier is null)
        {
            if (!File.Exists(full))
            {
                return new FieldChange(emission.Path, null, emission.Bytes(), ["written"]);
            }

            problems.Add($"{emission.Path} exists, and these fields need it written by the generator: move it away, or add the fields by hand.");

            return null;
        }

        if (earlier.Content == emission.Content)
        {
            return null;
        }

        if (!File.Exists(full))
        {
            if (recorded.Contains(emission.Path))
            {
                problems.Add($"{emission.Path}, which new-feature wrote and {ManifestPath(addition.After)} records, is gone, so the fields cannot reach it. Put it back.");
            }

            return null;
        }

        byte[] original = File.ReadAllBytes(full);
        var merged = SyntaxMerge.Merge(emission.Path, earlier.Content, emission.Content, original);

        if (merged.Problems.Count > 0)
        {
            problems.AddRange(merged.Problems);

            return null;
        }

        return new FieldChange(emission.Path, original, merged.Bytes!, merged.Changes);
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

    internal static List<TextEdits.Command> FieldSteps(string root, Addition addition)
    {
        string folder = System.IO.Path.Combine(root, PersistenceProject(addition.After), "Migrations");
        bool written = Directory.Exists(folder) && Directory.EnumerateFiles(folder, $"*_{addition.Migration}.cs").Any();
        var defaults = addition.Added.Where(field => field.Default is not null).ToList();

        return
        [
            .. written ? Array.Empty<TextEdits.Command>() : [TextEdits.ToolCommand(root, "build"), TextEdits.FromLastBuild(root, "migrations", "add", addition.Migration)],
            .. defaults.Count == 0
                ? Array.Empty<TextEdits.Command>()
                : [new TextEdits.Command(
                    $"new-field sets the defaultValue of {string.Join(", ", defaults.Select(field => $"{field.Name} ({field.Default!.Expression})"))} "
                        + $"in the AddColumn of the {addition.Migration} migration",
                    root,
                    _inProcess,
                    [])],
            .. addition.After.HasApi ? TextEdits.ContractSteps(root, built: !written) : [],
        ];
    }

    private static int FinishField(string root, Addition addition, TextWriter output, TextWriter error, IExecutor executor)
    {
        string done = $"the fields in every layer and in {ManifestPath(addition.After)}";

        int Run(TextEdits.Command command) =>
            command.FileName != _inProcess ? executor.Run(command) : SetDefaults(root, addition, output, error);

        if (TextEdits.RunAll(FieldSteps(root, addition), Run, output, error, done))
        {
            return 0;
        }

        error.WriteLine("Once the failure is fixed, the same new-field command resumes these steps, and writes nothing again.");

        return 1;
    }

    private static int SetDefaults(string root, Addition addition, TextWriter output, TextWriter error)
    {
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
        var applied = MigrationDefaults.Apply(text, NewField.TableOf(addition.After), addition.Added);

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
}

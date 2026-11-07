using System.Text;
using CleanArch.Cli.Generators.Shared;
using static CleanArch.Cli.Generators.Shared.ProjectFolders;
using static CleanArch.Cli.Generators.Shared.SourceText;

namespace CleanArch.Cli.Generators.RemoveFeature;

internal sealed class Removal(Analysis analysis, GenerationContext context) : Generation(context)
{
    private readonly string[] _deleted = [.. analysis.Deleted];

    protected override string Usage => FeatureRemoval.Usage;

    protected override string Unchanged => "Nothing was changed.";

    protected override string Writing => "removal";

    protected override IReadOnlyList<string> EditFailures => analysis.Blockers;

    protected override IReadOnlyList<(string Path, byte[] Bytes)> Created => [];

    protected override IReadOnlyList<(string Path, byte[] Original, byte[] Bytes)> Replaced =>
        [.. analysis.Undone.Select(pair => (pair.Key, pair.Value.Original, pair.Value.After))];

    protected override int? Concluded()
    {
        if (!analysis.HasManifest && analysis.Folders.Count == 0 && analysis.Hits.Count == 0)
        {
            Output.WriteLine($"No feature folder called {analysis.Features} is left under Src or Tests, and no manifest names it.");
            RemovalReport.PrintNotes(Output, analysis);

            return Succeeded;
        }

        if (!Context.DryRun)
        {
            return null;
        }

        RemovalReport.PrintReport(Output, Root, analysis);
        Output.WriteLine();
        Output.WriteLine(
            analysis.Blockers.Count == 0
                ? $"Nothing was changed. --apply removes what is listed above: {Tool.Name} remove feature {analysis.Features} --apply"
                : "Nothing was changed. --apply would refuse, for the reasons listed under Blockers.");

        return Succeeded;
    }

    protected override bool Draft(List<string> problems) => true;

    protected override int RefuseEdits(IReadOnlyList<string> failures)
    {
        RemovalReport.PrintReport(Output, Root, analysis);
        Error.WriteLine();
        Error.WriteLine($"error: {analysis.Features} was not removed, and nothing was changed: fix what Blockers lists, commit, then run this again.");

        if (analysis.Dependents.Count > 0)
        {
            Error.WriteLine($"error: {analysis.Features} holds the shared account-deletion files that {string.Join(", ", analysis.Dependents.Select(dependent => dependent.Name))} still depend on.");
        }

        return Failed;
    }

    protected override string? Preconditions() =>
        analysis.Migration is not null || HasInitialMigration(Root)
            ? MigrationCheck.Refusal(Root, Output, Context.Executor, $"The migration that removes {analysis.Features}")
            : null;

    protected override void Commit() => TextEdits.Commit(Root, Created, Replaced, _deleted);

    protected override void Report(ReportMode mode) =>
        Output.WriteLine(
            $"Removed {analysis.Features}: {RemovalReport.Count(_deleted.Length, "file")} deleted, its manifest and page among them, "
            + $"{RemovalReport.Count(analysis.Undos.Count(undo => undo.State == UndoState.Undo), "edit")} undone in {RemovalReport.Count(analysis.Undone.Count, "file")}, and the folders left empty.");

    protected override int Finish()
    {
        RemoveEmptyFolders();

        if (!TextEdits.RunAll(Steps(Root, analysis), Context.Executor.Run, Output, Error, $"{analysis.Features} removed, its edits undone"))
        {
            foreach (var step in ContractSteps(Root))
            {
                Error.WriteLine($"  {step.Display}");
            }

            return Failed;
        }

        if (analysis.Migration is { Schema: { } schema } migration && !DropSchema(Root, migration, schema))
        {
            Output.WriteLine($"{migration.Name} was not found to add DropSchema(\"{schema}\") to its Up and EnsureSchema(\"{schema}\") to its Down: add them by hand.");
        }

        var contract = ContractSteps(Root);

        if (contract.Count > 0 && !TextEdits.RunAll(contract, Context.Executor.Run, Output, Error, $"{analysis.Features} removed and its migration added"))
        {
            return Failed;
        }

        if (analysis.Notes.Count > 0)
        {
            Output.WriteLine();
            RemovalReport.PrintNotes(Output, analysis);
        }

        return Succeeded;
    }

    private void RemoveEmptyFolders()
    {
        foreach (string folder in analysis.Folders)
        {
            RemoveEmpty(Path.Combine(Root, folder));
            RemoveEmptyAncestors(Root, folder);
        }

        foreach (string file in _deleted)
        {
            RemoveEmptyAncestors(Root, file);
        }
    }

    private static bool HasInitialMigration(string root) =>
        FeatureRemoval.Persistence(root) is { } persistence
        && Directory.Exists(Path.Combine(root, persistence, "Migrations"))
        && Directory.EnumerateFiles(Path.Combine(root, persistence, "Migrations"), "*ModelSnapshot.cs").Any();

    internal static List<TextEdits.Command> Steps(string root, Analysis analysis) =>
    [
        TextEdits.ToolCommand(root, "docs-index"),
        TextEdits.ToolCommand(root, "build"),
        .. analysis.Migration is { } migration ? [TextEdits.FromLastBuild(root, "migrations", "add", migration.Name)] : Array.Empty<TextEdits.Command>(),
    ];

    internal static List<TextEdits.Command> ContractSteps(string root)
    {
        string presentation = Full(root, Presentation);

        if (!Directory.Exists(presentation) || !Directory.EnumerateDirectories(presentation, "*.Api").Any())
        {
            return [];
        }

        return TextEdits.ContractSteps(root, built: true);
    }

    private static bool DropSchema(string root, Migration migration, string schema)
    {
        string folder = Path.Combine(root, migration.Persistence, "Migrations");
        string? file = Directory.EnumerateFiles(folder, $"*_{migration.Name}.cs").Order(StringComparer.Ordinal).LastOrDefault();

        if (file is null)
        {
            return false;
        }

        byte[] bytes = File.ReadAllBytes(file);
        bool bom = bytes is [0xEF, 0xBB, 0xBF, ..];
        string text = Decode(bytes).ReplaceLineEndings("\n");
        const string Up = "protected override void Up(MigrationBuilder migrationBuilder)\n        {\n";
        const string Down = "protected override void Down(MigrationBuilder migrationBuilder)\n        {\n";
        int up = text.IndexOf(Up, StringComparison.Ordinal);
        int upEnd = up < 0 ? -1 : text.IndexOf("\n        }\n", up, StringComparison.Ordinal);

        if (upEnd < 0 || text.IndexOf(Down, StringComparison.Ordinal) < 0)
        {
            return false;
        }

        string ensure = $"            migrationBuilder.EnsureSchema(\n                name: \"{schema}\");\n";
        int down = text.IndexOf(Down, StringComparison.Ordinal) + Down.Length;

        if (!text[down..].Contains(ensure, StringComparison.Ordinal))
        {
            text = text.Insert(down, ensure + "\n");
        }

        text = text.Insert(upEnd, $"\n\n            migrationBuilder.DropSchema(\n                name: \"{schema}\");");
        File.WriteAllBytes(file, [.. bom ? Encoding.UTF8.Preamble : [], .. Encoding.UTF8.GetBytes(text)]);

        return true;
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

        while (!string.IsNullOrEmpty(folder) && folder is not ("Src" or "Tests" or "docs"))
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
}

using CleanArch.Cli.Generators.Shared;

namespace CleanArch.Cli.Generators.RemoveFeature;

internal static class RemovalReport
{
    internal static void PrintReport(TextWriter output, string root, Analysis analysis)
    {
        output.WriteLine($"Feature {analysis.Features}" + (analysis.Type is null ? " (no manifest, so its type is unknown)" : $" (type {analysis.Type}), from {FeatureManifest.PathOf(analysis.Features)}"));

        if (!analysis.HasManifest)
        {
            output.WriteLine();
            output.WriteLine($"Feature folders found ({analysis.Folders.Count}), none of them recorded by a manifest:");

            foreach (string folder in analysis.Folders)
            {
                output.WriteLine($"  {folder}");
            }

            output.WriteLine();
            PrintHits(output, analysis);
            PrintBlockers(output, analysis);

            return;
        }

        var unchanged = analysis.Owned.Except(analysis.Modified, StringComparer.Ordinal).Except(analysis.Missing, StringComparer.Ordinal).ToList();
        var undone = analysis.Undos.Where(undo => undo.State == UndoState.Undo).ToList();

        output.WriteLine();
        output.WriteLine($"--apply deletes the {unchanged.Count} file(s) the generators wrote, unchanged since, in:");

        foreach (string folder in analysis.Folders)
        {
            output.WriteLine($"  {folder}");
        }

        foreach (string file in unchanged.Where(file => !analysis.Folders.Any(folder => file.StartsWith(folder + "/", StringComparison.Ordinal))))
        {
            output.WriteLine($"  {file}");
        }

        output.WriteLine();
        output.WriteLine($"--apply undoes the {undone.Count} edit(s) the generators recorded, each exactly as it was made:");

        foreach (var undo in undone)
        {
            output.WriteLine($"  {undo.Edit.Path}: {Count(undo.Edit.Lines.Length, "line")} {(undo.Edit.Replaced.Length > 0 ? $"in place of {Count(undo.Edit.Replaced.Length, "line")} " : string.Empty)}{undo.Edit.Position} '{undo.Edit.Anchor.Trim()}'");
        }

        if (analysis.AddedFields.Count > 0)
        {
            output.WriteLine();
            output.WriteLine("Fields new-field added, which go with the files they changed:");

            foreach (var (declaration, added, paths) in analysis.AddedFields)
            {
                output.WriteLine($"  {declaration}, in {Count(paths.Length, "file")}, with its migration {added}");
            }
        }

        if (analysis.Page is not null)
        {
            output.WriteLine();
            output.WriteLine(analysis.PageGone
                ? $"Feature page: {analysis.Page} is already gone."
                : $"Feature page: {analysis.Page}, written by the generator; --apply deletes it, edited or not.");
        }

        if (analysis.Modified.Count + analysis.Extra.Count > 0)
        {
            output.WriteLine();
            output.WriteLine("--apply deletes these too, though they changed since the generators wrote them; git log and git show keep what they held:");

            foreach (string file in analysis.Modified)
            {
                output.WriteLine($"  Edited: {file}");
            }

            foreach (string file in analysis.Extra)
            {
                output.WriteLine($"  Added: {file}");
            }
        }

        var gone = analysis.Undos.Where(undo => undo.State == UndoState.Gone).ToList();

        if (analysis.Missing.Count + gone.Count > 0)
        {
            output.WriteLine();
            output.WriteLine("Already gone, so left as they are:");

            foreach (string file in analysis.Missing)
            {
                output.WriteLine($"  Missing: {file}");
            }

            foreach (var undo in gone)
            {
                output.WriteLine($"  Undone: {undo.Edit.Path}: {Count(undo.Edit.Lines.Length, "line")} {undo.Edit.Position} '{undo.Edit.Anchor.Trim()}'");
            }
        }

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

        output.WriteLine();
        output.WriteLine("Then --apply runs, from one build:");

        foreach (var step in Removal.Steps(root, analysis).Concat(Removal.ContractSteps(root)))
        {
            output.WriteLine($"  {step.Display}");
        }

        if (analysis.Migration is { } migration)
        {
            output.WriteLine(
                $"  {migration.Name} drops the tables of {analysis.Features}"
                + (migration.Schema is null ? string.Empty : $" and its schema \"{migration.Schema}\"")
                + $"; {string.Join(", ", [$"Add{analysis.Features}", .. analysis.AddedFields.Select(added => added.Migration)])}, which a database may have applied, "
                + (analysis.AddedFields.Count == 0 ? "stays as it is." : "stay as they are."));
        }

        output.WriteLine();
        PrintHits(output, analysis);
        PrintNotes(output, analysis);
        PrintBlockers(output, analysis);
    }

    internal static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    internal static void PrintHits(TextWriter output, Analysis analysis)
    {
        output.WriteLine(analysis.Hits.Count == 0
            ? "Code outside the feature that still uses it, recorded nowhere: none."
            : $"Code outside the feature that still uses it, recorded nowhere, which --apply refuses while it is there: {analysis.Hits.Count} line(s)");

        foreach (var hit in analysis.Hits)
        {
            output.WriteLine($"  {hit.Path}:{hit.Line}  {hit.Text}");
        }
    }

    internal static void PrintNotes(TextWriter output, Analysis analysis)
    {
        foreach (string group in new[] { "Configuration", "Documentation and samples" })
        {
            var notes = analysis.Notes.Where(note => note.Group == group).ToList();

            output.WriteLine(notes.Count == 0
                ? $"{group} outside the feature that still names it, recorded nowhere: none."
                : $"{group} outside the feature that still names it, recorded nowhere, left to you: {notes.Count} line(s)");

            foreach (var note in notes)
            {
                output.WriteLine($"  {note.Path}:{note.Line}  {note.Text}");
            }
        }
    }

    internal static void PrintBlockers(TextWriter output, Analysis analysis)
    {
        output.WriteLine();
        output.WriteLine(analysis.Blockers.Count == 0 ? "Blockers: none." : "Blockers:");

        foreach (string blocker in analysis.Blockers)
        {
            output.WriteLine($"  - {blocker}");
        }
    }
}

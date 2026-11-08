using CleanArch.Cli.Generators.Shared;

namespace CleanArch.Cli.Generators.RemoveFeature;

internal enum HitKind
{
    Composition,
    Model,
    Reference,
}

internal enum UndoState
{
    Undo,
    Gone,
    Displaced,
}

internal sealed record Hit(HitKind Kind, string Path, int Line, string Text);

internal sealed record Note(string Group, string Path, int Line, string Text);

internal sealed record Undo(TextEdits.Edit Edit, UndoState State);

internal sealed record Entry(string Path, string Sha256);

internal sealed record Manifest(
    string Type,
    List<Entry> Files,
    List<string> Shared,
    List<string> Consumers,
    string? Page,
    IReadOnlyList<TextEdits.Edit> Edits,
    IReadOnlyList<(string Declaration, string Migration, string[] Paths)> AddedFields);

internal sealed record Dependent(string Name, string How);

internal sealed record Migration(string Persistence, string Name, string? Schema);

internal sealed record Analysis(
    string Features,
    string? Type,
    bool HasManifest,
    List<string> Folders,
    List<string> Owned,
    List<string> Modified,
    List<string> Missing,
    List<string> Extra,
    List<Undo> Undos,
    Dictionary<string, (byte[] Original, byte[] After)> Undone,
    List<Hit> Hits,
    List<Note> Notes,
    List<string> Blockers,
    List<string> Shared,
    List<Dependent> Dependents,
    List<Hit> SharedUses,
    string? Page,
    bool PageGone,
    Migration? Migration,
    IReadOnlyList<(string Declaration, string Migration, string[] Paths)> AddedFields)
{
    internal IEnumerable<string> Deleted =>
        Owned.Except(Missing, StringComparer.Ordinal)
            .Concat(Extra)
            .Concat(Page is not null && !PageGone ? new[] { Page } : [])
            .Concat(HasManifest ? new[] { FeatureManifest.PathOf(Features) } : []);
}

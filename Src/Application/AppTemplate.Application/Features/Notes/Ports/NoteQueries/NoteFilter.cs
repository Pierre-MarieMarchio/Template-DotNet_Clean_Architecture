namespace AppTemplate.Application.Features.Notes.Ports.NoteQueries;

/// <summary>
/// What a caller may narrow a page of notes by: nothing yet. The paging foundation is generic over
/// a feature's filter, so the type exists for a first filterable field to be added to.
/// </summary>
public sealed record NoteFilter
{
    public static readonly NoteFilter None = new();
}

namespace AppTemplate.Application.Features.Notes.Policies;

/// <summary>
/// The size limits on a note's text. A note has no domain model to hold them, so they are declared
/// here once and read by the validator that enforces them and by the column definitions that match.
/// </summary>
public static class NoteInputPolicy
{
    /// <summary>The longest title, measured after trimming.</summary>
    public const int TitleMaxLength = 200;

    /// <summary>The longest body, in characters.</summary>
    public const int BodyMaxLength = 10_000;
}

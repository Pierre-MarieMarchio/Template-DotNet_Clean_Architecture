using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Features.Notes.Model.Errors;

public static class NoteErrors
{
    /// <summary>
    /// Also returned when the note belongs to somebody else: distinguishing the two would let a
    /// caller enumerate other users' note ids by comparing 403 against 404.
    /// </summary>
    public static Error NoteNotFound(Guid noteId) => Error.NotFound(
        "note.notFound",
        $"No note with id '{noteId}' was found.");
}

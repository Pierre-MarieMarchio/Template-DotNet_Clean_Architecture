using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Features.Notes.Model.Errors;

public static class NoteErrors
{
    public static Error NoteNotFound(Guid noteId) => Error.NotFound(
        "note.notFound",
        $"No note with id '{noteId}' was found.");
}

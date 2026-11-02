namespace AppTemplate.Application.Features.Notes.Ports.NoteStore;

public enum NoteChangeStatus
{
    Staged,

    NotFound,

    VersionMismatch,
}

namespace AppTemplate.Application.Features.Notes.Ports.NoteStore;

/// <summary>What staging a change to an existing note came to.</summary>
public enum NoteChangeStatus
{
    /// <summary>The change is staged and waits for the commit.</summary>
    Staged,

    /// <summary>The caller has no such note: it does not exist, or it is somebody else's.</summary>
    NotFound,

    /// <summary>The caller owns the note, and it is no longer at a version the caller named.</summary>
    VersionMismatch,
}

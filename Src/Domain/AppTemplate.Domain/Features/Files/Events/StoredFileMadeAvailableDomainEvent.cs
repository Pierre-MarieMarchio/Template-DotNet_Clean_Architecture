using AppTemplate.Domain.Common.Events;
using AppTemplate.Domain.Features.Files.ValueObjects;

namespace AppTemplate.Domain.Features.Files.Events;

/// <summary>
/// Raised when a deposit has been confirmed: the bytes are on the object store and they are the ones
/// that were declared. A consumer is entitled to assume the object at
/// <see cref="ObjectKey"/> exists and is complete — which is the whole reason this is raised at
/// confirmation and not at registration.
/// <para>
/// <b>Shaped for a delivery that may not happen.</b> This repository dispatches domain events
/// in-process, after commit, at most once, with no outbox, so a consumer may simply not run. What
/// makes that survivable here is that the intended effect — deriving thumbnails and other renditions
/// from a newly available file — re-derives its own precondition: a derivative is written at a key
/// computed from this one, so producing it twice writes the same object twice, and never producing
/// it leaves the file available but without renditions. Consistent, stale, and re-derivable by a
/// pass over available files whose renditions are missing. Nothing about whether the file may be
/// served depends on this event arriving.
/// </para>
/// <para>
/// <see cref="DeclaredMediaType"/> travels with the event so a consumer can decide whether the file
/// is its business without loading the aggregate. It is a claim, not a fact — see the type's own
/// documentation — so a consumer that acts on it must still confirm what the bytes are before
/// decoding them.
/// </para>
/// </summary>
public sealed record StoredFileMadeAvailableDomainEvent(
    Guid StoredFileId,
    Guid OwnerId,
    ObjectKey ObjectKey,
    DeclaredMediaType DeclaredMediaType,
    DateTimeOffset OccurredOn) : IDomainEvent;

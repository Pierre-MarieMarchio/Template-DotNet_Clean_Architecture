using AppTemplate.Domain.Common.Events;
using AppTemplate.Domain.Features.Files.ValueObjects;

namespace AppTemplate.Domain.Features.Files.Events;

/// <summary>
/// Raised when a file's row is being removed, so that its bytes can be reclaimed promptly.
/// <para>
/// <b>A fast path, not the correctness guarantee.</b> It reclaims storage now rather than at the
/// next sweep, and nothing depends on it having run. Storage is freed by comparing the object store
/// against the live rows and deleting what no row names — a sweep that needs no flag, no queue and
/// no delivery, and that covers a case this event cannot: bytes deposited against a signed URL whose
/// registration was never confirmed and has since been swept away.
/// </para>
/// <para>
/// Delivered twice it is delivered once: deleting an object that is already gone is a no-op on any
/// object store. Never delivered, the bytes stay until the sweep finds them unreferenced — stale
/// rather than incorrect, and unreachable in the meantime, because the row that named them is gone.
/// </para>
/// <para>
/// <see cref="ObjectKey"/> is carried rather than looked up, because by the time a consumer runs —
/// after the commit — the row it would have read no longer exists.
/// </para>
/// </summary>
public sealed record StoredFileDeletedDomainEvent(
    Guid StoredFileId,
    Guid OwnerId,
    ObjectKey ObjectKey,
    DateTimeOffset OccurredOn) : IDomainEvent;

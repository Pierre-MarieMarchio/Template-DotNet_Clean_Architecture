using AppTemplate.Domain.Core.Common.Events;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.ValueObjects;

namespace AppTemplate.Domain.Features.Files.Events;

/// <summary>
/// Raised when a file's row is being removed, so its bytes can be reclaimed promptly. A fast path
/// only: the orphan sweep reclaims them anyway, and a missed delivery leaves unreachable bytes
/// until the sweep. <see cref="ObjectKey"/> is carried because the row is gone by the time a
/// consumer runs.
/// </summary>
public sealed record StoredFileDeletedDomainEvent(
    Guid StoredFileId,
    UserId OwnerId,
    ObjectKey ObjectKey,
    DateTimeOffset OccurredOn) : IDomainEvent;

using AppTemplate.Domain.Core.Common.Events;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;

namespace AppTemplate.Domain.Features.Files.Model.Events;

/// <summary>Raised when a file's row is being removed, so its bytes can be reclaimed promptly — a
/// fast path only, since the orphan sweep reclaims them anyway. <see cref="ObjectKey"/> is carried
/// because the row is gone by the time a consumer runs.</summary>
public sealed record StoredFileDeletedDomainEvent(
    Guid StoredFileId,
    UserId OwnerId,
    ObjectKey ObjectKey,
    DateTimeOffset OccurredOn) : IDomainEvent;

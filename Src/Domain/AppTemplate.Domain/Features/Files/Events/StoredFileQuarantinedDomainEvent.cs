using AppTemplate.Domain.Core.Common.Events;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.ValueObjects;

namespace AppTemplate.Domain.Features.Files.Events;

/// <summary>
/// Raised when a file's content was examined and refused. It carries no reason, for the same
/// decision as <see cref="StoredFileState.Quarantined"/>.
/// <para>
/// Delivery is at most once with no outbox: the state is already committed and is what every read
/// consults, so a consumer may notify or alert but is never what makes the file unservable.
/// <see cref="ObjectKey"/> travels with it, but the row still names the key: the orphan sweep will
/// not reclaim these bytes, and deleting them destroys the only sample an investigation has.
/// </para>
/// </summary>
public sealed record StoredFileQuarantinedDomainEvent(
    Guid StoredFileId,
    UserId OwnerId,
    ObjectKey ObjectKey,
    DateTimeOffset OccurredOn) : IDomainEvent;

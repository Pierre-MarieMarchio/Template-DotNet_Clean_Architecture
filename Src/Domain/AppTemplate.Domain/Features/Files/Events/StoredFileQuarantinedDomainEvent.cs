using AppTemplate.Domain.Core.Common.Events;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.ValueObjects;

namespace AppTemplate.Domain.Features.Files.Events;

/// <summary>Raised when a file's content was examined and refused; it carries no reason, for the
/// same decision as <see cref="StoredFileState.Quarantined"/>. Delivery is at most once with no
/// outbox, so a consumer may notify or alert but is never what makes the file unservable. The row
/// still names the key, so the sweep will not reclaim the only sample an investigation has.</summary>
public sealed record StoredFileQuarantinedDomainEvent(
    Guid StoredFileId,
    UserId OwnerId,
    ObjectKey ObjectKey,
    DateTimeOffset OccurredOn) : IDomainEvent;

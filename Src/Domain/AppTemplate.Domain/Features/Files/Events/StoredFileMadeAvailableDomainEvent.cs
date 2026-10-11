using AppTemplate.Domain.Core.Common.Events;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.ValueObjects;

namespace AppTemplate.Domain.Features.Files.Events;

/// <summary>Raised when a file's content was examined and accepted, so a consumer may assume the
/// object at <see cref="ObjectKey"/> exists and is complete. Delivery is at most once with no
/// outbox, so nothing about serving depends on it: a consumer must re-derive its precondition (see
/// <c>docs/DECISIONS.md</c>). <see cref="DeclaredMediaType"/> is a claim, not a measurement.</summary>
public sealed record StoredFileMadeAvailableDomainEvent(
    Guid StoredFileId,
    UserId OwnerId,
    ObjectKey ObjectKey,
    DeclaredMediaType DeclaredMediaType,
    DateTimeOffset OccurredOn) : IDomainEvent;

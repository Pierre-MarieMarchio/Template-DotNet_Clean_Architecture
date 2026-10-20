using AppTemplate.Domain.Core.Common.Events;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;

namespace AppTemplate.Domain.Features.Files.Model.Events;

public sealed record StoredFileMadeAvailableDomainEvent(
    Guid StoredFileId,
    UserId OwnerId,
    ObjectKey ObjectKey,
    DeclaredMediaType DeclaredMediaType,
    DateTimeOffset OccurredOn) : IDomainEvent;

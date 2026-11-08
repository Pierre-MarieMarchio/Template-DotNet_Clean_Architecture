using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;

namespace AppTemplate.Domain.Features.Files.Model.Entities;

public sealed class StoredFileSnapshot
{
    public required Guid Id { get; init; }

    public required UserId OwnerId { get; init; }

    public required FileCategory Category { get; init; }

    public required ObjectKey ObjectKey { get; init; }

    public required StoredFileName Name { get; init; }

    public required DeclaredMediaType DeclaredMediaType { get; init; }

    public required FileSize Size { get; init; }

    public required Sha256Checksum Checksum { get; init; }

    public required StoredFileState State { get; init; }

    public required DateTimeOffset RegisteredAt { get; init; }

    public required DateTimeOffset? AvailableAt { get; init; }

    public required int InspectionAttempts { get; init; }

    public required DateTimeOffset? NextInspectionAt { get; init; }

    public required IEnumerable<string> Tags { get; init; }
}

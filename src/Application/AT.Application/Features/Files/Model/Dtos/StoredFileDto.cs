using AppTemplate.Domain.Features.Files.Model.ValueObjects;

namespace AppTemplate.Application.Features.Files.Model.Dtos;

public sealed record StoredFileDto(
    Guid Id,
    string Category,
    string Name,
    string DeclaredMediaType,
    long SizeInBytes,
    string Checksum,
    StoredFileState State,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? AvailableAt,
    IReadOnlyList<string> Tags);

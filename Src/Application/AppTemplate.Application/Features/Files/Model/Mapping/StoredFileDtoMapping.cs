using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Features.Files.Model.Dtos;
using AppTemplate.Domain.Features.Files.Model.Entities;

namespace AppTemplate.Application.Features.Files.Model.Mapping;

internal static class StoredFileDtoMapping
{
    public static Versioned<StoredFileDto> ToVersioned(StoredFile storedFile)
    {
        ArgumentNullException.ThrowIfNull(storedFile);

        return new Versioned<StoredFileDto>(ToDto(storedFile), storedFile.Version);
    }

    public static StoredFileDto ToDto(StoredFile storedFile)
    {
        ArgumentNullException.ThrowIfNull(storedFile);

        return new StoredFileDto(
            storedFile.Id,
            storedFile.Category.Value,
            storedFile.Name.Value,
            storedFile.DeclaredMediaType.Value,
            storedFile.Size.Bytes,
            storedFile.Checksum.Value,
            storedFile.State,
            storedFile.RegisteredAt,
            storedFile.AvailableAt,
            [.. storedFile.Tags.Select(tag => tag.Value)]);
    }
}

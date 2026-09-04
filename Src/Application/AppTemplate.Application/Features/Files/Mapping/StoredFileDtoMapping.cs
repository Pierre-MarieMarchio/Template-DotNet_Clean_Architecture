using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Features.Files.Dtos;
using AppTemplate.Domain.Features.Files.Entities;

namespace AppTemplate.Application.Features.Files.Mapping;

/// <summary>
/// Turns the aggregate a command just wrote into the same shape a read would have produced, without
/// a second query — the same reasoning as <c>TodoListDtoMapping</c>, and sound for the same reason:
/// the tracker writes the store-assigned version and audit stamps back onto the aggregate inside
/// <c>SaveChangesAsync</c>, so once that call has returned the values here are the ones the row was
/// committed with.
/// </summary>
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
            storedFile.Name.Value,
            storedFile.DeclaredMediaType.Value,
            storedFile.Size.Bytes,
            storedFile.Checksum.Value,
            storedFile.State,
            storedFile.RegisteredAt,
            storedFile.AvailableAt);
    }
}

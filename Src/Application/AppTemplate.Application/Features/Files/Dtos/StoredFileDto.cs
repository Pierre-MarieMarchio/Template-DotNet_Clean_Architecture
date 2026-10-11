using AppTemplate.Domain.Features.Files.ValueObjects;

namespace AppTemplate.Application.Features.Files.Dtos;

/// <summary>A stored file as every read of it produces it. It carries no content and no URL:
/// reading the bytes is a separate act that mints a short-lived grant or streams them.</summary>
/// <param name="Category">The configured category the file was registered under.</param>
/// <param name="DeclaredMediaType">What the client said the file is: a claim the content was checked
/// against when the file is available, an unchecked one otherwise. Never a measurement.</param>
/// <param name="SizeInBytes">Declared while pending, agreed with the store from deposited onwards.</param>
/// <param name="AvailableAt">Null until the content has been examined and cleared, and for ever for a
/// refused file.</param>
/// <param name="Tags">The owner's labels, normalised and de-duplicated by the domain.</param>
/// <param name="State">Where the file is in its life: <see cref="StoredFileState.Deposited"/> means
/// "wait", <see cref="StoredFileState.Quarantined"/> means "never".</param>
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

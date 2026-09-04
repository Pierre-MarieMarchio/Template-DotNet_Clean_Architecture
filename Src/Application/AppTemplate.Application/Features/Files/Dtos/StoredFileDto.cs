using AppTemplate.Domain.Features.Files.ValueObjects;

namespace AppTemplate.Application.Features.Files.Dtos;

/// <summary>
/// A stored file as every read of it produces: the list, the single read, and the projection of the
/// aggregate a command just wrote.
/// <para>
/// One shape rather than a summary and a detail, because the aggregate is flat and a second shape
/// would differ only by which fields somebody guessed a list would not want. It carries no content
/// and no URL: reading the bytes is a separate, deliberate act that mints a short-lived grant.
/// </para>
/// </summary>
/// <param name="DeclaredMediaType">
/// What the client said the file is. On a <see cref="StoredFileState.Available"/> file it is a claim
/// the content was checked against and did not contradict; on any other it is an unchecked claim.
/// Either way it is not a measurement — see the value object's own documentation for what may and
/// may not be decided from it.
/// </param>
/// <param name="SizeInBytes">Declared while <see cref="StoredFileState.Pending"/>, and agreed with
/// the store from <see cref="StoredFileState.Deposited"/> onwards.</param>
/// <param name="AvailableAt"><c>null</c> until the content has been examined and cleared, and for
/// ever for a file that was never deposited against or whose content was refused.</param>
/// <param name="State">Where the file is in its life, and the only thing that says whether asking
/// for its content is worth doing. <see cref="StoredFileState.Deposited"/> means "wait";
/// <see cref="StoredFileState.Quarantined"/> means "never".</param>
public sealed record StoredFileDto(
    Guid Id,
    string Name,
    string DeclaredMediaType,
    long SizeInBytes,
    string Checksum,
    StoredFileState State,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? AvailableAt);

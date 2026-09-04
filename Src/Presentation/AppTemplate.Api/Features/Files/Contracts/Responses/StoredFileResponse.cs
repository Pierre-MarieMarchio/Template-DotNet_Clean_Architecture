namespace AppTemplate.Api.Features.Files.Contracts.Responses;

/// <summary>
/// The wire shape of one file: everything about it except its content, which this API never serves.
/// One shape for the list, the single read and the body of a confirmation — the aggregate is flat,
/// so a summary would differ from a detail only by fields somebody guessed a list would not want.
/// </summary>
/// <param name="DeclaredMediaType">
/// What the client said the file is, never confirmed against the bytes by anything here. The name
/// says so, so a consumer that decides from it knows it is deciding from a claim.
/// </param>
/// <param name="SizeInBytes">Declared while <c>pending</c>, and agreed with the store once
/// <c>available</c>.</param>
/// <param name="Checksum">SHA-256 of the content, as 64 lower-case hexadecimal characters. Worth
/// carrying: it is what a client verifies its own download against.</param>
/// <param name="Status">
/// <c>pending</c> or <c>available</c> — a string rather than the domain enum's declaration order,
/// so no client ever depends on the order members happen to be written in.
/// </param>
/// <param name="AvailableAt"><c>null</c> until the deposit is confirmed, and for ever if it never
/// is.</param>
public sealed record StoredFileResponse(
    Guid Id,
    string Name,
    string DeclaredMediaType,
    long SizeInBytes,
    string Checksum,
    string Status,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? AvailableAt);

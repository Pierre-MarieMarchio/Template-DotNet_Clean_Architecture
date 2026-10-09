namespace AppTemplate.Api.Features.Files.Contracts.Responses;

/// <summary>
/// The wire shape of one file, without its content (see <c>GET .../content</c>). One shape serves
/// the list, the single read and the body of a confirmation or upload.
/// </summary>
/// <param name="Category">The category the file was registered under, as configured in
/// <c>Files:Categories</c>.</param>
/// <param name="DeclaredMediaType">What the client said the file is; checked against the bytes only
/// when the category verifies signatures.</param>
/// <param name="SizeInBytes">Declared while <c>pending</c>, agreed with the store once deposited.</param>
/// <param name="Checksum">SHA-256 of the content, 64 lower-case hexadecimal characters; what a client
/// verifies its download against.</param>
/// <param name="Status"><c>pending</c>, <c>deposited</c>, <c>available</c> or <c>quarantined</c>, as a
/// string so no client depends on the enum's order.</param>
/// <param name="AvailableAt"><c>null</c> until the file is released by inspection.</param>
public sealed record StoredFileResponse(
    Guid Id,
    string Category,
    string Name,
    string DeclaredMediaType,
    long SizeInBytes,
    string Checksum,
    string Status,
    DateTimeOffset RegisteredAt,
    DateTimeOffset? AvailableAt,
    IReadOnlyList<string> Tags);

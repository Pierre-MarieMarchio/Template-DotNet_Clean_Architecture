namespace AppTemplate.Api.Features.Files.Contracts.Responses;

/// <summary>
/// What registering a file hands back: its id and the right to deposit its bytes, in one response so
/// there is no window where the file exists and cannot be filled.
/// </summary>
/// <remarks>
/// No status, size or checksum: they are the client's own declaration. <c>GET</c> the file for them
/// and for the <c>ETag</c> a conditional confirmation or deletion needs.
/// </remarks>
public sealed record StoredFileRegistrationResponse(Guid Id, UploadGrantResponse Upload);

namespace AppTemplate.Api.Features.Files.Contracts.Responses;

/// <summary>
/// What registering a file hands back: the id it will be known by, and the right to deposit its
/// bytes.
/// </summary>
/// <remarks>
/// Both at once, because a client that had to ask twice would have a window in which the file
/// exists and cannot be filled.
/// <para>
/// It carries no <c>status</c>, no size and no checksum: those are the client's own declaration,
/// echoed back would say only that this API can repeat what it was told. <c>GET</c> the file to read
/// them as stored, and to get the entity tag a conditional confirmation or deletion needs.
/// </para>
/// </remarks>
public sealed record StoredFileRegistrationResponse(Guid Id, UploadGrantResponse Upload);

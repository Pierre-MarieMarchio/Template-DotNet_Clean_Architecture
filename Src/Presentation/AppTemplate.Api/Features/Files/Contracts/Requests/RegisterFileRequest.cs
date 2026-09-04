namespace AppTemplate.Api.Features.Files.Contracts.Requests;

/// <summary>
/// What the client says about a file it is about to deposit. Metadata only — the bytes never travel
/// through this API, so this body is a few hundred characters whatever the file weighs.
/// </summary>
/// <param name="Name">A label to show, and the name a download is offered under. It addresses
/// nothing: the object key is minted by the domain and never leaves it.</param>
/// <param name="DeclaredMediaType">
/// Named "declared" on the way in as well as on the way out, because nothing in this feature ever
/// reads the bytes to check it. A client that learns the word here is not surprised to meet it in
/// the response.
/// </param>
/// <param name="SizeInBytes">Bound into the upload grant, so a deposit of a different length is
/// refused by the store rather than at confirmation.</param>
/// <param name="Checksum">
/// SHA-256 of the content, 64 hexadecimal characters. Asked for now rather than at confirmation:
/// a digest supplied after the upload would be the client agreeing with itself.
/// </param>
public sealed record RegisterFileRequest(
    string Name,
    string DeclaredMediaType,
    long SizeInBytes,
    string Checksum);

using System.Text.Json.Serialization;

namespace AppTemplate.Api.Features.Files.Contracts.Requests;

/// <summary>What the client says about a file it is about to deposit directly: metadata only, the bytes go to the store.</summary>
/// <param name="Category">One of the categories configured in <c>Files:Categories</c>; it decides
/// the media types, size, upload modes and quota that apply.</param>
/// <param name="Name">A label to show and the name a download is offered under; it addresses
/// nothing.</param>
/// <param name="DeclaredMediaType">A claim, checked against the leading bytes by the background
/// inspection when the category verifies signatures.</param>
/// <param name="SizeInBytes">Bound into the upload grant, so the store refuses a deposit of another
/// length. Required: an omitted length would bind to zero and read as a rejected file.</param>
/// <param name="Checksum">SHA-256 of the content, 64 hexadecimal characters, bound into the grant.
/// Asked for at registration: a digest supplied afterwards would be the client agreeing with itself.</param>
public sealed record RegisterFileRequest(
    string Category,
    string Name,
    string DeclaredMediaType,
    [property: JsonRequired] long SizeInBytes,
    string Checksum);

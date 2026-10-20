using System.Text.Json.Serialization;

namespace AppTemplate.Api.Features.Files.Contracts.Requests;

/// <summary>The metadata of a file about to be deposited directly on the store.</summary>
/// <param name="Category">One of the categories configured in <c>Files:Categories</c>.</param>
/// <param name="Name">A label to show, and the name a download is offered under.</param>
/// <param name="DeclaredMediaType">
/// Checked against the leading bytes when the category verifies signatures.
/// </param>
/// <param name="SizeInBytes">The exact length the store will accept for the deposit.</param>
/// <param name="Checksum">SHA-256 of the content, 64 hexadecimal characters.</param>
public sealed record RegisterFileRequest(
    string Category,
    string Name,
    string DeclaredMediaType,
    [property: JsonRequired] long SizeInBytes,
    string Checksum);

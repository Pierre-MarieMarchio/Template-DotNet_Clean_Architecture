namespace AppTemplate.Application.Features.Files.UseCases.Commands.RegisterFile;

/// <summary>
/// What the client claims about a file it is about to deposit: size and checksum are checked against
/// the store at confirmation, the media type against the content at inspection.
/// </summary>
/// <param name="Category">The configured category; it decides media types, size, upload modes and quota.</param>
/// <param name="Name">A label to show and to offer as a download name. It addresses nothing.</param>
/// <param name="Checksum">SHA-256 of the content, as 64 hexadecimal characters. Declared at registration so the compared value is committed before the bytes exist.</param>
public sealed record RegisterFileCommand(
    string Category,
    string Name,
    string MediaType,
    long SizeInBytes,
    string Checksum);

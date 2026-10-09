namespace AppTemplate.Application.Features.Files.UseCases.Commands.RegisterFile;

/// <summary>
/// Everything the client claims about a file it is about to deposit. <paramref name="SizeInBytes"/>
/// and <paramref name="Checksum"/> are checked against the store at confirmation;
/// <paramref name="MediaType"/> against the content at inspection when the category verifies
/// signatures.
/// </summary>
/// <param name="Category">The configured category the file belongs to; it decides the media types,
/// size, upload modes and quota that apply.</param>
/// <param name="Name">A label to show and to offer as a download name. It addresses nothing.</param>
/// <param name="Checksum">SHA-256 of the content, as 64 hexadecimal characters. Asked for up front
/// rather than at confirmation so that the value being compared was committed before the bytes
/// existed: a checksum supplied afterwards would be a client agreeing with itself.</param>
public sealed record RegisterFileCommand(
    string Category,
    string Name,
    string MediaType,
    long SizeInBytes,
    string Checksum);

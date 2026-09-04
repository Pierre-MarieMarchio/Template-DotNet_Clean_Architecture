namespace AppTemplate.Application.Features.Files.UseCases.Commands.RegisterFile;

/// <summary>
/// Everything the client claims about a file it is about to deposit. Every field is a claim, and
/// two of them — <paramref name="SizeInBytes"/> and <paramref name="Checksum"/> — are checked
/// against the store before the file can be read back. The third is never checked at all; see
/// <c>DeclaredMediaType</c>.
/// </summary>
/// <param name="Name">A label to show and to offer as a download name. It addresses nothing.</param>
/// <param name="Checksum">SHA-256 of the content, as 64 hexadecimal characters. Asked for up front
/// rather than at confirmation so that the value being compared was committed before the bytes
/// existed: a checksum supplied afterwards would be a client agreeing with itself.</param>
public sealed record RegisterFileCommand(
    string Name,
    string MediaType,
    long SizeInBytes,
    string Checksum);

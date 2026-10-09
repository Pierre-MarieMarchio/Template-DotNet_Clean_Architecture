namespace AppTemplate.Application.Features.Files.UseCases.Commands.UploadFile;

/// <summary>A file whose bytes arrive with the request; the checksum is computed from them.</summary>
/// <param name="Category">The configured category the file belongs to.</param>
/// <param name="Name">A label to show and to offer as a download name. It addresses nothing.</param>
/// <param name="MediaType">The media type the client declares for the content.</param>
/// <param name="SizeInBytes">The length of <paramref name="Content"/>.</param>
/// <param name="Content">The bytes: seekable, positioned at the start, exactly
/// <paramref name="SizeInBytes"/> long. The caller owns and disposes it.</param>
public sealed record UploadFileCommand(
    string Category,
    string Name,
    string MediaType,
    long SizeInBytes,
    Stream Content);

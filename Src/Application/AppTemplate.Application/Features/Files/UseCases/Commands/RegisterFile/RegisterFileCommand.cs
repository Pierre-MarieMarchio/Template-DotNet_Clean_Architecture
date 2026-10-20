namespace AppTemplate.Application.Features.Files.UseCases.Commands.RegisterFile;

public sealed record RegisterFileCommand(
    string Category,
    string Name,
    string MediaType,
    long SizeInBytes,
    string Checksum);

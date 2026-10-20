namespace AppTemplate.Application.Features.Files.UseCases.Commands.UploadFile;

public sealed record UploadFileCommand(
    string Category,
    string Name,
    string MediaType,
    long SizeInBytes,
    Stream Content);

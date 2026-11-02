using AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.RegisterFile;

public sealed record RegisterFileOutcome(Guid StoredFileId, IssuedUploadGrant Upload);

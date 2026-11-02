using AppTemplate.Application.Core.Common.Concurrency;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.DeleteStoredFile;

public sealed record DeleteStoredFileCommand(Guid StoredFileId, VersionPrecondition? Precondition = null);

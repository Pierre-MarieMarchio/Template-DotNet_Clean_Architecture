using AppTemplate.Application.Core.Common.Concurrency;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.ConfirmFileUpload;

public sealed record ConfirmFileUploadCommand(Guid StoredFileId, VersionPrecondition? Precondition = null);

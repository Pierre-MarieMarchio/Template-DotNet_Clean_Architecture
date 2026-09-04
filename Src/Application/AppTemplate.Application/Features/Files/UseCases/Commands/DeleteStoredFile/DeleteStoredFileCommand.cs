using AppTemplate.Application.Common.Concurrency;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.DeleteStoredFile;

/// <param name="Precondition">
/// The versions the caller will accept, or <c>null</c> for an unconditional delete.
/// </param>
public sealed record DeleteStoredFileCommand(Guid StoredFileId, VersionPrecondition? Precondition = null);

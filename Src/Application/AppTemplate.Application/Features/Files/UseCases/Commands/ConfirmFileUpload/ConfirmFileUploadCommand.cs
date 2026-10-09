using AppTemplate.Application.Core.Common.Concurrency;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.ConfirmFileUpload;

/// <summary>
/// Carries nothing about the deposit: what was uploaded is read from the store, not from the caller.
/// </summary>
/// <param name="Precondition">
/// The versions the caller will accept, or <c>null</c> for an unconditional confirmation.
/// </param>
public sealed record ConfirmFileUploadCommand(Guid StoredFileId, VersionPrecondition? Precondition = null);

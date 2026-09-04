using AppTemplate.Application.Common.Concurrency;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.ConfirmFileUpload;

/// <summary>
/// Carries nothing about the deposit. What was uploaded is read from the store, not from the caller:
/// a client repeating its own declaration would confirm only that it can repeat itself.
/// </summary>
/// <param name="Precondition">
/// The versions the caller will accept, or <c>null</c> for an unconditional confirmation.
/// </param>
public sealed record ConfirmFileUploadCommand(Guid StoredFileId, VersionPrecondition? Precondition = null);

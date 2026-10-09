using AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.RegisterFile;

/// <summary>
/// What registering hands back: the id to confirm with and the right to deposit the bytes, together
/// so that a file never exists without a way to fill it.
/// </summary>
/// <param name="Upload">The port's own record, carried rather than restated field by field.</param>
public sealed record RegisterFileOutcome(Guid StoredFileId, IssuedUploadGrant Upload);

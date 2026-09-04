using AppTemplate.Application.Features.Files.Ports.FileContentStore;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.RegisterFile;

/// <summary>
/// What registering hands back: the id the file will be known by, and the right to deposit its
/// bytes. Both are needed at once — the id to confirm with afterwards, the grant to upload against
/// now — and a client that had to ask twice would have a window in which the file exists and cannot
/// be filled.
/// </summary>
/// <param name="Upload">
/// The port's own record, carried rather than copied field by field. Re-stating a URL and an expiry
/// here would be one fact written twice, and the second copy is where they drift apart.
/// </param>
public sealed record RegisterFileOutcome(Guid StoredFileId, IssuedUploadGrant Upload);

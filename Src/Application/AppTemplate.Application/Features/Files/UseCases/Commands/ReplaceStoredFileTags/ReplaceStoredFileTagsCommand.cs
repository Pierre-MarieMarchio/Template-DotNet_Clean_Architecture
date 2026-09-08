using AppTemplate.Application.Core.Common.Concurrency;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.ReplaceStoredFileTags;

/// <param name="Tags">The complete set the file should end up with; anything not in it is removed.</param>
/// <param name="Precondition">
/// The versions the caller will accept, or <c>null</c> for an unconditional replacement.
/// </param>
public sealed record ReplaceStoredFileTagsCommand(
    Guid StoredFileId,
    IReadOnlyList<string> Tags,
    VersionPrecondition? Precondition = null);

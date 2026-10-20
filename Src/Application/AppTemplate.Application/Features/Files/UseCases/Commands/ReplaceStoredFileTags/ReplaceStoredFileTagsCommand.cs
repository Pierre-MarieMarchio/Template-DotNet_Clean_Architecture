using AppTemplate.Application.Core.Common.Concurrency;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.ReplaceStoredFileTags;

public sealed record ReplaceStoredFileTagsCommand(
    Guid StoredFileId,
    IReadOnlyList<string> Tags,
    VersionPrecondition? Precondition = null);

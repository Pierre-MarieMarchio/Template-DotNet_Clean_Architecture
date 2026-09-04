namespace AppTemplate.Application.Features.Files.UseCases.Queries.GetStoredFile;

/// <summary>The metadata of one file. Never its content — reading that is a separate act.</summary>
public sealed record GetStoredFileQuery(Guid StoredFileId);

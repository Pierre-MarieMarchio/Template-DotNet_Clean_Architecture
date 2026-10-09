namespace AppTemplate.Application.Features.Files.UseCases.Queries.IssueFileDownload;

/// <summary>
/// Asks for one file's content. A query: minting a signature or opening a read changes nothing, so
/// the endpoint above it is a <c>GET</c>.
/// </summary>
public sealed record IssueFileDownloadQuery(Guid StoredFileId);

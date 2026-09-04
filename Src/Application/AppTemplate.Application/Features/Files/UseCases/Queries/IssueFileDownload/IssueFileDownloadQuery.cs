namespace AppTemplate.Application.Features.Files.UseCases.Queries.IssueFileDownload;

/// <summary>
/// Asks for the right to read one file's content.
/// <para>
/// A query rather than a command, and the distinction is not bookkeeping: minting a signed URL
/// computes a signature and changes nothing anywhere, so the endpoint above it is a <c>GET</c> that
/// answers <c>302</c>. Filing it as a command would make reading a file a write, and a write is not
/// something a browser may follow.
/// </para>
/// </summary>
public sealed record IssueFileDownloadQuery(Guid StoredFileId);

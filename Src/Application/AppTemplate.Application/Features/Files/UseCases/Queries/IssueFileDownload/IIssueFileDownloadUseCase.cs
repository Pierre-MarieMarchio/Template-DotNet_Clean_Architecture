using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;

namespace AppTemplate.Application.Features.Files.UseCases.Queries.IssueFileDownload;

/// <summary>
/// Answers with the port's own grant rather than a shape of this operation's — there is nothing to
/// add to a URL and an expiry, and a wrapper carrying the same two fields would be one fact stated
/// twice.
/// </summary>
public interface IIssueFileDownloadUseCase : IUseCase<IssueFileDownloadQuery, Result<IssuedDownloadGrant>>;

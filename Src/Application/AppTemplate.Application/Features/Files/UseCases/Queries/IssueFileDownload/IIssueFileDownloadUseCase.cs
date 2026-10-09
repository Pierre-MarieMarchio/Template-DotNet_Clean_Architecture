using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Features.Files.UseCases.Queries.IssueFileDownload;

/// <summary>
/// Answers with a signed URL to redirect to when the store supports direct transfer, and with the
/// opened content otherwise.
/// </summary>
public interface IIssueFileDownloadUseCase : IUseCase<IssueFileDownloadQuery, Result<FileDownload>>;

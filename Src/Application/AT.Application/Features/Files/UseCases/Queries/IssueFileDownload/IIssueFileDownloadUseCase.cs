using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Features.Files.UseCases.Queries.IssueFileDownload;

public interface IIssueFileDownloadUseCase : IUseCase<IssueFileDownloadQuery, Result<FileDownload>>;

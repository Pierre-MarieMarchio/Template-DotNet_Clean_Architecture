using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.ReclaimOrphanedContent;

/// <summary>
/// One sweep over the whole store, so it never reads <c>ICurrentUser</c>. Answers with the number of
/// objects deleted.
/// </summary>
public interface IReclaimOrphanedContentUseCase : IUseCase<Result<int>>;

using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Features.Files.UseCases.Commands.ReclaimOrphanedContent;

public interface IReclaimOrphanedContentUseCase : IUseCase<Result<int>>;

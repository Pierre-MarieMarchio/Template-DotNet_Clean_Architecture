using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Features.Files.UseCases.Queries.GetUsedFileTags;

public interface IGetUsedFileTagsUseCase : IUseCase<Result<IReadOnlyList<string>>>;

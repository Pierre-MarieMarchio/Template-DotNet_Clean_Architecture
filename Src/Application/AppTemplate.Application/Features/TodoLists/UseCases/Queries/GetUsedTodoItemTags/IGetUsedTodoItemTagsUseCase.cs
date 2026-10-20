using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetUsedTodoItemTags;

public interface IGetUsedTodoItemTagsUseCase : IUseCase<Result<IReadOnlyList<string>>>;

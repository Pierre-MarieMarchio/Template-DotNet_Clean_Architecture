using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetUsedTodoItemTags;

/// <summary>
/// The tags this caller has already used on their items, for a picker or a filter. The owner comes
/// from <see cref="ICurrentUser"/> and there is nothing else to ask for, so the operation takes no
/// request.
/// </summary>
public interface IGetUsedTodoItemTagsUseCase : IUseCase<Result<IReadOnlyList<string>>>;

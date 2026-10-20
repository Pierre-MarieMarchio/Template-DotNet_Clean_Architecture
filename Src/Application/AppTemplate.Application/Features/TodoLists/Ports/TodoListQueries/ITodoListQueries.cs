using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.TodoLists.Model.Dtos;
using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Features.TodoLists.Ports.TodoListQueries;

public interface ITodoListQueries
{
    Task<PagedResult<TodoListSummaryDto>> GetForOwnerAsync(
        UserId ownerId,
        FeaturePageRequest<TodoListFilter> request,
        CancellationToken cancellationToken = default);

    Task<Versioned<TodoListDetailDto>?> GetDetailAsync(
        Guid id,
        UserId ownerId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserId>> ListDataOwnerIdsAsync(CancellationToken cancellationToken = default);
}

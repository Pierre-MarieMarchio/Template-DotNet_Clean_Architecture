using AppTemplate.Application.Common.Tagging;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.TodoLists.Ports.TodoItemTagQueries;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetUsedTodoItemTags;

public sealed class GetUsedTodoItemTagsUseCase(
    ITodoItemTagQueries queries,
    ICacheStore cache,
    ICurrentUser currentUser) : IGetUsedTodoItemTagsUseCase
{
    public async Task<Result<IReadOnlyList<string>>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<IReadOnlyList<string>>();
        }

        return Result.Success(await cache.GetOrCreateAsync(
            UsedTagsCache.KeyFor(UsedTagsCache.TodoItemScope, userId.Value),
            token => queries.GetUsedTagsForOwnerAsync(userId.Value, token),
            UsedTagsCache.Lifetime,
            cancellationToken));
    }
}

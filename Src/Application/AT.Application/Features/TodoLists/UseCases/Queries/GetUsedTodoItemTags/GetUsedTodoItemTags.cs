using AppTemplate.Application.Common.Tagging;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.TodoLists.Ports.TodoItemTagQueries;
using Mediator;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetUsedTodoItemTags;

public sealed record GetUsedTodoItemTagsQuery : IQuery<Result<IReadOnlyList<string>>>;

public sealed class GetUsedTodoItemTagsQueryHandler(
    ITodoItemTagQueries queries,
    ICacheStore cache,
    ICurrentUser currentUser) : IQueryHandler<GetUsedTodoItemTagsQuery, Result<IReadOnlyList<string>>>
{
    public async ValueTask<Result<IReadOnlyList<string>>> Handle(GetUsedTodoItemTagsQuery query, CancellationToken cancellationToken)
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

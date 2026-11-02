using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.TodoLists.Model.Dtos;
using AppTemplate.Application.Features.TodoLists.Model.Errors;
using AppTemplate.Application.Features.TodoLists.Ports.TodoListQueries;
using Mediator;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoList;

public sealed record GetTodoListQuery(Guid TodoListId) : IQuery<Result<Versioned<TodoListDetailDto>>>;

public sealed class GetTodoListQueryHandler(
    ITodoListQueries queries,
    ICurrentUser currentUser) : IQueryHandler<GetTodoListQuery, Result<Versioned<TodoListDetailDto>>>
{
    public async ValueTask<Result<Versioned<TodoListDetailDto>>> Handle(
        GetTodoListQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var userId = currentUser.RequireUserId();

        if (userId.IsFailure)
        {
            return userId.To<Versioned<TodoListDetailDto>>();
        }

        var detail = await queries.GetDetailAsync(query.TodoListId, userId.Value, cancellationToken);

        return detail is null
            ? Result.Failure<Versioned<TodoListDetailDto>>(TodoListErrors.ListNotFound(query.TodoListId))
            : detail;
    }
}

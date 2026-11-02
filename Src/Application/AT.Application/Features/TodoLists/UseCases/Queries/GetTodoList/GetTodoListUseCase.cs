using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.Validation;
using AppTemplate.Application.Features.TodoLists.Model.Dtos;
using AppTemplate.Application.Features.TodoLists.Model.Errors;
using AppTemplate.Application.Features.TodoLists.Ports.TodoListQueries;
using FluentValidation;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoList;

public sealed class GetTodoListUseCase(
    ITodoListQueries queries,
    ICurrentUser currentUser,
    IValidator<GetTodoListQuery> validator) : IGetTodoListUseCase
{
    public async Task<Result<Versioned<TodoListDetailDto>>> ExecuteAsync(
        GetTodoListQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = await validator.EnsureValidAsync(query, cancellationToken);

        if (validation.IsFailure)
        {
            return validation.To<Versioned<TodoListDetailDto>>();
        }

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

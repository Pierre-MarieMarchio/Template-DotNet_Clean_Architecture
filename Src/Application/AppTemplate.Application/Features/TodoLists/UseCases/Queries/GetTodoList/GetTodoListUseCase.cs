using AppTemplate.Application.Common.Concurrency;
using AppTemplate.Application.Common.Ports;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Common.Validation;
using AppTemplate.Application.Features.TodoLists.Dtos;
using AppTemplate.Application.Features.TodoLists.Errors;
using AppTemplate.Application.Features.TodoLists.Ports.TodoListQueries;
using FluentValidation;

namespace AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoList;

public sealed class GetTodoListUseCase(
    ITodoListQueries queries,
    ICurrentUser currentUser,
    IValidator<GetTodoListQuery> validator) : IGetTodoListUseCase
{
    /// <returns>
    /// The list, and the version a caller has to name to change it. Nothing here decides how that
    /// version is published — the transport does, and it is the only layer that should.
    /// </returns>
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

        // Ownership goes into the query, not a check afterwards: fetching first would already have
        // pulled another user's data into memory.
        var detail = await queries.GetDetailAsync(query.TodoListId, userId.Value, cancellationToken);

        return detail is null
            ? Result.Failure<Versioned<TodoListDetailDto>>(TodoListErrors.ListNotFound(query.TodoListId))
            : detail;
    }
}

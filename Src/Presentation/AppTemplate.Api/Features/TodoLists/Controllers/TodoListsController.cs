using AppTemplate.Api.Common.Contracts;
using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.Common.Idempotency;
using AppTemplate.Api.Features.TodoLists.Contracts.Mapping;
using AppTemplate.Api.Features.TodoLists.Contracts.Requests;
using AppTemplate.Api.Features.TodoLists.Contracts.Responses;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.CreateTodoList;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.DeleteTodoList;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.RenameTodoList;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoList;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoLists;
using Microsoft.AspNetCore.Mvc;

namespace AppTemplate.Api.Features.TodoLists.Controllers;

[Route("api/v{version:apiVersion}/todo-lists")]
[Asp.Versioning.ApiVersion("1.0")]
[ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
public sealed class TodoListsController(
    IGetTodoListsUseCase getTodoLists,
    IGetTodoListUseCase getTodoList,
    ICreateTodoListUseCase createTodoList,
    IRenameTodoListUseCase renameTodoList,
    IDeleteTodoListUseCase deleteTodoList) : ApiControllerBase
{
    /// <summary>Lists the caller's own todo lists, sorted, filtered and paginated.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PagedResponse<TodoListSummaryResponse>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    public async Task<ActionResult<PagedResponse<TodoListSummaryResponse>>> GetAll(
        [FromQuery] GetTodoListsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var query = new GetTodoListsQuery(
            request.Paging,
            request.Page,
            request.PageSize,
            request.Cursor,
            request.Sort,
            request.Search,
            request.CreatedAfter,
            request.CreatedBefore);

        return OkOrProblem(TodoListResponseMapping.ToPageResponse(await getTodoLists.ExecuteAsync(query, cancellationToken)));
    }

    /// <summary>Gets one todo list with its items, and the <c>ETag</c> needed to change it.</summary>
    [HttpGet("{todoListId:guid}", Name = nameof(GetTodoList))]
    [HttpHead("{todoListId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TodoListResponse))]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<TodoListResponse>> GetTodoList(
        Guid todoListId,
        CancellationToken cancellationToken)
    {
        var query = new GetTodoListQuery(todoListId);

        return OkOrProblem(TodoListResponseMapping.ToListResponse(await getTodoList.ExecuteAsync(query, cancellationToken)));
    }

    /// <summary>Creates a todo list owned by the caller.</summary>
    [HttpPost]
    [Idempotent]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(TodoListResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<TodoListResponse>> Create(
        [FromBody] CreateTodoListRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new CreateTodoListCommand(request.Name);
        var result = TodoListResponseMapping.ToListResponse(await createTodoList.ExecuteAsync(command, cancellationToken));

        return CreatedOrProblem(result, nameof(GetTodoList), list => new { todoListId = list.Id });
    }

    /// <summary>Renames a todo list.</summary>
    [HttpPut("{todoListId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TodoListResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<TodoListResponse>> Rename(
        Guid todoListId,
        [FromBody] RenameTodoListRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (ReadPrecondition(out var precondition, out bool requiresExistence) is { } refusal)
        {
            return refusal;
        }

        var command = new RenameTodoListCommand(todoListId, request.Name, precondition);
        var result = RequiringExistence(
            requiresExistence,
            await renameTodoList.ExecuteAsync(command, cancellationToken));

        return UpdatedOrProblem(TodoListResponseMapping.ToListResponse(result));
    }

    /// <summary>Deletes a todo list and its items.</summary>
    [HttpDelete("{todoListId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> Delete(Guid todoListId, CancellationToken cancellationToken)
    {
        if (ReadPrecondition(out var precondition, out bool requiresExistence) is { } refusal)
        {
            return refusal;
        }

        var command = new DeleteTodoListCommand(todoListId, precondition);
        var result = RequiringExistence(
            requiresExistence,
            await deleteTodoList.ExecuteAsync(command, cancellationToken));

        return NoContentOrProblem(result);
    }
}

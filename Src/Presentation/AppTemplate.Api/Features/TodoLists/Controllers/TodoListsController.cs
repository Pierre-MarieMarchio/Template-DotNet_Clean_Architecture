using AppTemplate.Api.Core.Common.Contracts;
using AppTemplate.Api.Core.Common.Controllers;
using AppTemplate.Api.Core.Common.Idempotency;
using AppTemplate.Api.Features.TodoLists.Contracts.Requests;
using AppTemplate.Api.Features.TodoLists.Contracts.Responses;
using AppTemplate.Api.Features.TodoLists.Mapping;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.CreateTodoList;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.DeleteTodoList;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.RenameTodoList;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoList;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoLists;
using Microsoft.AspNetCore.Mvc;

namespace AppTemplate.Api.Features.TodoLists.Controllers;

/// <summary>
/// The lists themselves: the collection, one list with its items, and the three writes that create,
/// rename and destroy one.
/// </summary>
/// <remarks>
/// Every read that names one aggregate publishes its version as a strong <c>ETag</c>, and every
/// write of one honours <c>If-Match</c>. See <c>docs/ADDING-A-FEATURE.md</c> for the conventions
/// this surface shares with the others.
/// <para>
/// Three controllers under one route prefix, and the prefix is what makes them one surface: the
/// aggregate boundary is in the URLs, not in the classes. No route reaches an item without naming
/// the list that owns it, whichever class answers it.
/// </para>
/// </remarks>
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
    /// <remarks>
    /// Two paging modes: <c>offset</c> (the default), addressed by <c>page</c>/<c>pageSize</c> and
    /// answering a <c>totalCount</c>; and <c>cursor</c>, addressed by an opaque <c>cursor</c> token
    /// minted by the previous page's <c>nextCursor</c>, which never counts the whole match set.
    /// <c>sort</c> is a comma-separated list of whitelisted fields (<c>name</c>, <c>createdAt</c>,
    /// <c>lastModifiedAt</c>), each optionally suffixed <c>:asc</c>/<c>:desc</c>; cursor mode allows
    /// at most one. <c>search</c> matches the list name, case-insensitively, as a contains;
    /// <c>createdAfter</c>/<c>createdBefore</c> narrow by creation date.
    /// <para>
    /// No <c>ETag</c>: a page of summaries is not one aggregate, so there is no single version that
    /// describes it and no write that <c>If-Match</c> could guard.
    /// </para>
    /// </remarks>
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
    [HttpGet("{todoListId:guid}", Name = nameof(GetById))]
    [HttpHead("{todoListId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TodoListResponse))]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<TodoListResponse>> GetById(
        Guid todoListId,
        CancellationToken cancellationToken)
    {
        var query = new GetTodoListQuery(todoListId);

        return OkOrProblem(TodoListResponseMapping.ToListResponse(await getTodoList.ExecuteAsync(query, cancellationToken)));
    }

    /// <summary>Creates a todo list owned by the caller.</summary>
    /// <remarks>
    /// No <c>If-Match</c>: the resource does not exist yet, so there is no version to name. Two
    /// callers creating lists are not competing for one.
    /// <para>
    /// Idempotent: send an <c>Idempotency-Key</c> header to make a retried request safe. Repeating
    /// the same key with the same body returns the first response again, carrying
    /// <c>Idempotency-Replayed: true</c>, instead of creating a second list; repeating it with a
    /// different body is refused with <c>idempotency.keyReused</c>.
    /// </para>
    /// </remarks>
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

        return CreatedOrProblem(result, nameof(GetById), list => new { todoListId = list.Id });
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

        // The id comes from the route, never the body, so two sources of truth cannot disagree.
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

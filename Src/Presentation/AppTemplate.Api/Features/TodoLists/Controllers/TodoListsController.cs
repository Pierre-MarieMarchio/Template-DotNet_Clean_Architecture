using AppTemplate.Api.Common.Contracts;
using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.Common.Idempotency;
using AppTemplate.Api.Features.TodoLists.Contracts.Requests;
using AppTemplate.Api.Features.TodoLists.Contracts.Responses;
using AppTemplate.Api.Features.TodoLists.Mapping;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.AddTagToTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.AddTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.CompleteTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.CreateTodoList;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.DeleteTodoList;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.RemoveTagFromTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.RemoveTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.RenameTodoList;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.ReopenTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.ReplaceTodoItemTags;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.UpdateTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoItems;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoList;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoLists;
using Microsoft.AspNetCore.Mvc;

namespace AppTemplate.Api.Features.TodoLists.Controllers;

/// <summary>
/// The TodoList aggregate's HTTP surface. Items are addressed through their list, which is what the
/// aggregate boundary means: no route can reach an item without naming the list that owns it.
/// </summary>
/// <remarks>
/// Authorisation is not declared here: <c>Program.cs</c> installs a fallback policy requiring an
/// authenticated user, so every endpoint is protected unless it opts out with
/// <c>[AllowAnonymous]</c>.
/// <para>
/// Response statuses are declared action by action rather than once for the controller. 409 is
/// reachable only from a write — a violated aggregate invariant, or a row another request changed
/// between the read and the commit — and 404 only where an aggregate has to be found first;
/// declaring either on a read would put a status in the OpenAPI document that the endpoint cannot
/// produce. 400 is declared wherever a body, a query string or an <c>If-Match</c> header is read.
/// 401 sits on the controller because every action here requires authentication; 413, 415, 429 and
/// 500 come from <see cref="ApiControllerBase"/>.
/// </para>
/// <para>
/// <b>Conditional requests.</b> Every read that names one aggregate publishes its version as a
/// strong <c>ETag</c>, and every write of one honours <c>If-Match</c>. The comparison itself
/// belongs to the use case, which is the only place holding the aggregate it loaded and can therefore
/// compare without leaving a window for somebody else to commit; what this controller does is decode
/// the header into a <c>VersionPrecondition</c> and hand it over. The transport verdicts on the
/// header — malformed, or missing where configuration requires one — come from
/// <c>ApiControllerBase.ReadPrecondition</c>.
/// </para>
/// <para>
/// <b>Bodies.</b> Every write answers with the representation it produced and that representation's
/// new <c>ETag</c>, so a caller never has to re-read what it just changed to keep writing. Nothing
/// here serialises an application DTO: <see cref="TodoListResponseMapping"/> projects onto this feature's own
/// contracts.
/// </para>
/// </remarks>
[Route("api/v{version:apiVersion}/todo-lists")]
[Asp.Versioning.ApiVersion("1.0")]
[ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
public sealed class TodoListsController(
    IGetTodoListsUseCase getTodoLists,
    IGetTodoListUseCase getTodoList,
    IGetTodoItemsUseCase getTodoItems,
    IGetTodoItemUseCase getTodoItem,
    ICreateTodoListUseCase createTodoList,
    IRenameTodoListUseCase renameTodoList,
    IDeleteTodoListUseCase deleteTodoList,
    IAddTodoItemUseCase addTodoItem,
    IUpdateTodoItemUseCase updateTodoItem,
    ICompleteTodoItemUseCase completeTodoItem,
    IReopenTodoItemUseCase reopenTodoItem,
    IRemoveTodoItemUseCase removeTodoItem,
    IAddTagToTodoItemUseCase addTagToTodoItem,
    IReplaceTodoItemTagsUseCase replaceTodoItemTags,
    IRemoveTagFromTodoItemUseCase removeTagFromTodoItem) : ApiControllerBase
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

    /// <summary>Gets every item of a todo list.</summary>
    /// <remarks>
    /// The <c>ETag</c> is the list's, because the list is the aggregate: the version published here is
    /// the one a caller writes against, and it also covers the list's own fields.
    /// <para>
    /// No paging parameters and no filters, so there is nothing to reject as malformed: the aggregate
    /// is bounded by <c>TodoList.MaxItems</c> and this endpoint reads neither a body nor a query string.
    /// </para>
    /// </remarks>
    [HttpGet("{todoListId:guid}/items")]
    [HttpHead("{todoListId:guid}/items")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TodoItemsResponse))]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<TodoItemsResponse>> GetItems(
        Guid todoListId,
        CancellationToken cancellationToken)
    {
        var query = new GetTodoItemsQuery(todoListId);

        return OkOrProblem(TodoListResponseMapping.ToItemsResponse(await getTodoItems.ExecuteAsync(query, cancellationToken)));
    }

    /// <summary>Gets one item of a todo list.</summary>
    /// <remarks>
    /// The <c>ETag</c> is the list's, because the list is the aggregate: a caller holding this item
    /// may not assume the rest of the list stood still.
    /// </remarks>
    [HttpGet("{todoListId:guid}/items/{todoItemId:guid}", Name = nameof(GetItemById))]
    [HttpHead("{todoListId:guid}/items/{todoItemId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TodoItemResponse))]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<TodoItemResponse>> GetItemById(
        Guid todoListId,
        Guid todoItemId,
        CancellationToken cancellationToken)
    {
        var query = new GetTodoItemQuery(todoListId, todoItemId);

        return OkOrProblem(TodoListResponseMapping.ToItemResponse(await getTodoItem.ExecuteAsync(query, cancellationToken)));
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

    /// <summary>Adds an item to a todo list.</summary>
    /// <remarks>
    /// Idempotent: send an <c>Idempotency-Key</c> header to make a retried request safe. Repeating
    /// the same key with the same body returns the first response again instead of adding a second
    /// item; repeating it with a different body is refused with <c>idempotency.keyReused</c>.
    /// </remarks>
    [HttpPost("{todoListId:guid}/items")]
    [Idempotent]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(TodoItemResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<TodoItemResponse>> AddItem(
        Guid todoListId,
        [FromBody] AddTodoItemRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (ReadPrecondition(out var precondition, out bool requiresExistence) is { } refusal)
        {
            return refusal;
        }

        var command = new AddTodoItemCommand(
            todoListId,
            request.Title,
            request.Description,
            request.Tags,
            precondition);

        var result = TodoListResponseMapping.ToItemResponse(
            RequiringExistence(requiresExistence, await addTodoItem.ExecuteAsync(command, cancellationToken)));

        // Location addresses the item that was created, which is what the body carries.
        return CreatedOrProblem(result, nameof(GetItemById), item => new { todoListId, todoItemId = item.Id });
    }

    /// <summary>Replaces an item's title and description.</summary>
    /// <remarks>
    /// A <c>PUT</c> carrying the complete title/description representation, not a <c>PATCH</c>: every
    /// write on this surface is a named operation on the aggregate —
    /// there is no <c>PATCH</c>. An omitted description therefore clears the one stored.
    /// </remarks>
    [HttpPut("{todoListId:guid}/items/{todoItemId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TodoItemResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<TodoItemResponse>> UpdateItem(
        Guid todoListId,
        Guid todoItemId,
        [FromBody] UpdateTodoItemRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (ReadPrecondition(out var precondition, out bool requiresExistence) is { } refusal)
        {
            return refusal;
        }

        var command = new UpdateTodoItemCommand(
            todoListId,
            todoItemId,
            request.Title,
            request.Description,
            precondition);

        var result = RequiringExistence(
            requiresExistence,
            await updateTodoItem.ExecuteAsync(command, cancellationToken));

        return UpdatedOrProblem(TodoListResponseMapping.ToItemResponse(result));
    }

    /// <summary>Marks an item as completed.</summary>
    [HttpPost("{todoListId:guid}/items/{todoItemId:guid}/complete")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TodoItemResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<TodoItemResponse>> CompleteItem(
        Guid todoListId,
        Guid todoItemId,
        CancellationToken cancellationToken)
    {
        if (ReadPrecondition(out var precondition, out bool requiresExistence) is { } refusal)
        {
            return refusal;
        }

        var command = new CompleteTodoItemCommand(todoListId, todoItemId, precondition);
        var result = RequiringExistence(
            requiresExistence,
            await completeTodoItem.ExecuteAsync(command, cancellationToken));

        return UpdatedOrProblem(TodoListResponseMapping.ToItemResponse(result));
    }

    /// <summary>Marks a completed item as not completed again.</summary>
    [HttpPost("{todoListId:guid}/items/{todoItemId:guid}/reopen")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TodoItemResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<TodoItemResponse>> ReopenItem(
        Guid todoListId,
        Guid todoItemId,
        CancellationToken cancellationToken)
    {
        if (ReadPrecondition(out var precondition, out bool requiresExistence) is { } refusal)
        {
            return refusal;
        }

        var command = new ReopenTodoItemCommand(todoListId, todoItemId, precondition);
        var result = RequiringExistence(
            requiresExistence,
            await reopenTodoItem.ExecuteAsync(command, cancellationToken));

        return UpdatedOrProblem(TodoListResponseMapping.ToItemResponse(result));
    }

    /// <summary>Removes an item from a todo list.</summary>
    /// <remarks>
    /// Answers with the list rather than the item: the item addressed by this route no longer exists,
    /// so the list is the only representation left to return — and the only one whose <c>ETag</c> the
    /// caller can go on writing against.
    /// </remarks>
    [HttpDelete("{todoListId:guid}/items/{todoItemId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TodoListResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<TodoListResponse>> RemoveItem(
        Guid todoListId,
        Guid todoItemId,
        CancellationToken cancellationToken)
    {
        if (ReadPrecondition(out var precondition, out bool requiresExistence) is { } refusal)
        {
            return refusal;
        }

        var command = new RemoveTodoItemCommand(todoListId, todoItemId, precondition);
        var result = RequiringExistence(
            requiresExistence,
            await removeTodoItem.ExecuteAsync(command, cancellationToken));

        return UpdatedOrProblem(TodoListResponseMapping.ToListResponse(result));
    }

    /// <summary>Adds one tag to an item.</summary>
    /// <remarks>
    /// 200 rather than 201: adding a tag the item already carries is a no-op in the domain, and there
    /// is no <c>GET .../tags/{tag}</c> for a <c>Location</c> to name. The caller gets the resulting
    /// item, so it can tell what the item ended up with either way.
    /// </remarks>
    [HttpPost("{todoListId:guid}/items/{todoItemId:guid}/tags")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TodoItemResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<TodoItemResponse>> AddItemTag(
        Guid todoListId,
        Guid todoItemId,
        [FromBody] AddTodoItemTagRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (ReadPrecondition(out var precondition, out bool requiresExistence) is { } refusal)
        {
            return refusal;
        }

        var command = new AddTagToTodoItemCommand(todoListId, todoItemId, request.Tag, precondition);
        var result = RequiringExistence(
            requiresExistence,
            await addTagToTodoItem.ExecuteAsync(command, cancellationToken));

        return UpdatedOrProblem(TodoListResponseMapping.ToItemResponse(result));
    }

    /// <summary>Replaces an item's whole tag set.</summary>
    /// <remarks>
    /// An empty list clears the tags, and this is also the only way to remove a tag containing a
    /// <c>/</c> — see the limit described on <c>DELETE .../tags/{tag}</c>.
    /// </remarks>
    [HttpPut("{todoListId:guid}/items/{todoItemId:guid}/tags")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TodoItemResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<TodoItemResponse>> ReplaceItemTags(
        Guid todoListId,
        Guid todoItemId,
        [FromBody] ReplaceTodoItemTagsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (ReadPrecondition(out var precondition, out bool requiresExistence) is { } refusal)
        {
            return refusal;
        }

        var command = new ReplaceTodoItemTagsCommand(todoListId, todoItemId, request.Tags, precondition);
        var result = RequiringExistence(
            requiresExistence,
            await replaceTodoItemTags.ExecuteAsync(command, cancellationToken));

        return UpdatedOrProblem(TodoListResponseMapping.ToItemResponse(result));
    }

    /// <summary>Removes one tag from an item.</summary>
    /// <remarks>
    /// The tag is named in the route, so a caller has to percent-encode it: a tag is free text — any
    /// non-blank characters, 50 at most, lower-cased by the domain.
    /// <para>
    /// <b>A tag containing a <c>/</c> cannot be addressed here at all</b>, because <c>%2F</c> is
    /// decoded before routing and would split the path. Remove such a tag by sending the set without
    /// it to <c>PUT .../tags</c>.
    /// </para>
    /// </remarks>
    [HttpDelete("{todoListId:guid}/items/{todoItemId:guid}/tags/{tag}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TodoItemResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<TodoItemResponse>> RemoveItemTag(
        Guid todoListId,
        Guid todoItemId,
        string tag,
        CancellationToken cancellationToken)
    {
        if (ReadPrecondition(out var precondition, out bool requiresExistence) is { } refusal)
        {
            return refusal;
        }

        var command = new RemoveTagFromTodoItemCommand(todoListId, todoItemId, tag, precondition);
        var result = RequiringExistence(
            requiresExistence,
            await removeTagFromTodoItem.ExecuteAsync(command, cancellationToken));

        return UpdatedOrProblem(TodoListResponseMapping.ToItemResponse(result));
    }
}

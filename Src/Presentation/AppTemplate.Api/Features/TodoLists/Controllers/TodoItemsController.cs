using AppTemplate.Api.Core.Common.Controllers;
using AppTemplate.Api.Core.Common.Idempotency;
using AppTemplate.Api.Features.TodoLists.Contracts.Requests;
using AppTemplate.Api.Features.TodoLists.Contracts.Responses;
using AppTemplate.Api.Features.TodoLists.Mapping;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.AddTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.CompleteTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.RemoveTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.ReopenTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.UpdateTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoItems;
using Microsoft.AspNetCore.Mvc;

namespace AppTemplate.Api.Features.TodoLists.Controllers;

/// <summary>
/// The items of a list: reading them, adding one, replacing its fields, completing and reopening
/// it, and removing it.
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
public sealed class TodoItemsController(
    IGetTodoItemsUseCase getTodoItems,
    IGetTodoItemUseCase getTodoItem,
    IAddTodoItemUseCase addTodoItem,
    IUpdateTodoItemUseCase updateTodoItem,
    ICompleteTodoItemUseCase completeTodoItem,
    IReopenTodoItemUseCase reopenTodoItem,
    IRemoveTodoItemUseCase removeTodoItem) : ApiControllerBase
{
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
}

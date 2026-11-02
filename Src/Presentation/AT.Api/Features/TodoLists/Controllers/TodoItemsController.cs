using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.Common.Idempotency;
using AppTemplate.Api.Features.TodoLists.Contracts.Mapping;
using AppTemplate.Api.Features.TodoLists.Contracts.Requests;
using AppTemplate.Api.Features.TodoLists.Contracts.Responses;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.AddTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.CompleteTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.RemoveTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.ReopenTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.UpdateTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetTodoItems;
using Microsoft.AspNetCore.Mvc;

namespace AppTemplate.Api.Features.TodoLists.Controllers;

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

        return CreatedOrProblem(result, nameof(GetItemById), item => new { todoListId, todoItemId = item.Id });
    }

    /// <summary>Replaces an item's title and description.</summary>
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

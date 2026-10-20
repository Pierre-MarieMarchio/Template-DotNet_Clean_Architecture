using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.Features.TodoLists.Contracts.Mapping;
using AppTemplate.Api.Features.TodoLists.Contracts.Requests;
using AppTemplate.Api.Features.TodoLists.Contracts.Responses;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.AddTagToTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.RemoveTagFromTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.ReplaceTodoItemTags;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetUsedTodoItemTags;
using Microsoft.AspNetCore.Mvc;

namespace AppTemplate.Api.Features.TodoLists.Controllers;

[Route("api/v{version:apiVersion}/todo-lists")]
[Asp.Versioning.ApiVersion("1.0")]
[ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
public sealed class TodoItemTagsController(
    IAddTagToTodoItemUseCase addTagToTodoItem,
    IReplaceTodoItemTagsUseCase replaceTodoItemTags,
    IRemoveTagFromTodoItemUseCase removeTagFromTodoItem,
    IGetUsedTodoItemTagsUseCase getUsedTags) : ApiControllerBase
{
    /// <summary>Adds one tag to an item.</summary>
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

    /// <summary>The tags the caller has already used on their items.</summary>
    [HttpGet("tags")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(UsedTagsResponse))]
    public async Task<ActionResult<UsedTagsResponse>> GetUsedTags(
        CancellationToken cancellationToken = default) =>
        OkOrProblem(TodoListResponseMapping.ToUsedTagsResponse(
            await getUsedTags.ExecuteAsync(cancellationToken)));
}

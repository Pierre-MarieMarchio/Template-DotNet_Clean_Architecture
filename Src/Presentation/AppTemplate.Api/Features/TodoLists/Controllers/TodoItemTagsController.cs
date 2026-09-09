using AppTemplate.Api.Core.Common.Controllers;
using AppTemplate.Api.Features.TodoLists.Contracts.Requests;
using AppTemplate.Api.Features.TodoLists.Contracts.Responses;
using AppTemplate.Api.Features.TodoLists.Mapping;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.AddTagToTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.RemoveTagFromTodoItem;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands.ReplaceTodoItemTags;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries.GetUsedTodoItemTags;
using Microsoft.AspNetCore.Mvc;

namespace AppTemplate.Api.Features.TodoLists.Controllers;

/// <summary>
/// The labels an item carries, and the labels a caller has already used.
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
public sealed class TodoItemTagsController(
    IAddTagToTodoItemUseCase addTagToTodoItem,
    IReplaceTodoItemTagsUseCase replaceTodoItemTags,
    IRemoveTagFromTodoItemUseCase removeTagFromTodoItem,
    IGetUsedTodoItemTagsUseCase getUsedTags) : ApiControllerBase
{
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

    /// <summary>The tags the caller has already used on their items.</summary>
    /// <remarks>
    /// For a picker or a filter. Served from a cache with a short lifetime, so a tag added
    /// moments ago may be missing: this is a suggestion, and no write depends on it.
    /// </remarks>
    [HttpGet("tags")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(UsedTagsResponse))]
    public async Task<ActionResult<UsedTagsResponse>> GetUsedTags(
        CancellationToken cancellationToken = default) =>
        OkOrProblem(TodoListResponseMapping.ToUsedTagsResponse(
            await getUsedTags.ExecuteAsync(cancellationToken)));
}

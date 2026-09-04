using AppTemplate.Api.Common.Concurrency;
using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.Common.Errors;
using AppTemplate.Api.Features.TodoLists.Contracts;
using AppTemplate.Application.Common;
using AppTemplate.Application.Features.TodoLists.Dtos;
using AppTemplate.Application.Features.TodoLists.Errors;
using AppTemplate.Application.Features.TodoLists.UseCases.Commands;
using AppTemplate.Application.Features.TodoLists.UseCases.Queries;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

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
/// 401 sits on the controller because every action here requires authentication; 429 and 500 come
/// from <see cref="ApiControllerBase"/>.
/// </para>
/// <para>
/// <b>Conditional requests.</b> Every read of a single resource publishes the aggregate's version as
/// a strong <c>ETag</c>, and every write of one honours <c>If-Match</c>. The comparison itself is not
/// made here: the header is decoded into a <c>VersionPrecondition</c> and handed to the use case,
/// which is the only place that holds the aggregate it loaded and can therefore compare without
/// leaving a window for somebody else to commit. What this controller decides is transport: whether
/// a missing header is allowed, whether a present one is well-formed, and which status each outcome
/// gets.
/// </para>
/// </remarks>
[Route("api/v{version:apiVersion}/todo-lists")]
[Asp.Versioning.ApiVersion("1.0")]
[ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
public sealed class TodoListsController(
    IGetTodoListsUseCase getTodoLists,
    IGetTodoListUseCase getTodoList,
    IGetTodoItemUseCase getTodoItem,
    ICreateTodoListUseCase createTodoList,
    IRenameTodoListUseCase renameTodoList,
    IDeleteTodoListUseCase deleteTodoList,
    IAddTodoItemUseCase addTodoItem,
    ICompleteTodoItemUseCase completeTodoItem,
    IRemoveTodoItemUseCase removeTodoItem,
    IOptions<ConcurrencyOptions> concurrency) : ApiControllerBase
{
    /// <summary>Lists the caller's own todo lists, paginated.</summary>
    /// <remarks>
    /// No <c>ETag</c>: a page of summaries is not one aggregate, so there is no single version that
    /// describes it and no write that <c>If-Match</c> could guard.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(PagedResult<TodoListSummaryDto>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<PagedResult<TodoListSummaryDto>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        OkOrProblem(await getTodoLists.ExecuteAsync(new GetTodoListsQuery(page, pageSize), cancellationToken));

    /// <summary>Gets one todo list with its items, and the <c>ETag</c> needed to change it.</summary>
    [HttpGet("{todoListId:guid}", Name = nameof(GetById))]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TodoListDetailDto))]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<TodoListDetailDto>> GetById(
        Guid todoListId,
        CancellationToken cancellationToken) =>
        Validated(await getTodoList.ExecuteAsync(todoListId, cancellationToken));

    /// <summary>Gets one item of a todo list.</summary>
    /// <remarks>
    /// The <c>ETag</c> is the list's, because the list is the aggregate: a caller holding this item
    /// may not assume the rest of the list stood still.
    /// </remarks>
    [HttpGet("{todoListId:guid}/items/{todoItemId:guid}", Name = nameof(GetItemById))]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(TodoItemDto))]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<TodoItemDto>> GetItemById(
        Guid todoListId,
        Guid todoItemId,
        CancellationToken cancellationToken) =>
        Validated(await getTodoItem.ExecuteAsync(new GetTodoItemQuery(todoListId, todoItemId), cancellationToken));

    /// <summary>Creates a todo list owned by the caller.</summary>
    /// <remarks>
    /// No <c>If-Match</c>: the resource does not exist yet, so there is no version to name. Two
    /// callers creating lists are not competing for one.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(Guid))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> Create(
        [FromBody] CreateTodoListCommand command,
        CancellationToken cancellationToken)
    {
        var result = await createTodoList.ExecuteAsync(command, cancellationToken);

        return CreatedOrProblem(result, nameof(GetById), new { todoListId = result.IsSuccess ? result.Value : default });
    }

    /// <summary>Renames a todo list.</summary>
    [HttpPut("{todoListId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> Rename(
        Guid todoListId,
        [FromBody] RenameTodoListRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var precondition = IfMatchPrecondition.Read(Request);

        if (Refused(precondition) is { } refusal)
        {
            return refusal;
        }

        // The id comes from the route, never the body, so two sources of truth cannot disagree.
        var command = new RenameTodoListCommand(todoListId, request.Name, precondition.Required);

        return NoContentOrProblem(
            Existing(precondition, await renameTodoList.ExecuteAsync(command, cancellationToken)));
    }

    /// <summary>Deletes a todo list and its items.</summary>
    [HttpDelete("{todoListId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> Delete(Guid todoListId, CancellationToken cancellationToken)
    {
        var precondition = IfMatchPrecondition.Read(Request);

        if (Refused(precondition) is { } refusal)
        {
            return refusal;
        }

        var command = new DeleteTodoListCommand(todoListId, precondition.Required);

        return NoContentOrProblem(
            Existing(precondition, await deleteTodoList.ExecuteAsync(command, cancellationToken)));
    }

    /// <summary>Adds an item to a todo list.</summary>
    [HttpPost("{todoListId:guid}/items")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(Guid))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> AddItem(
        Guid todoListId,
        [FromBody] AddTodoItemRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var precondition = IfMatchPrecondition.Read(Request);

        if (Refused(precondition) is { } refusal)
        {
            return refusal;
        }

        var command = new AddTodoItemCommand(
            todoListId,
            request.Title,
            request.Description,
            request.Tags,
            precondition.Required);

        var result = Existing(precondition, await addTodoItem.ExecuteAsync(command, cancellationToken));

        // Location addresses the item that was created, which is what the body carries.
        return CreatedOrProblem(
            result,
            nameof(GetItemById),
            new { todoListId, todoItemId = result.IsSuccess ? result.Value : default });
    }

    /// <summary>Marks an item as completed.</summary>
    [HttpPost("{todoListId:guid}/items/{todoItemId:guid}/complete")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> CompleteItem(
        Guid todoListId,
        Guid todoItemId,
        CancellationToken cancellationToken)
    {
        var precondition = IfMatchPrecondition.Read(Request);

        if (Refused(precondition) is { } refusal)
        {
            return refusal;
        }

        var command = new CompleteTodoItemCommand(todoListId, todoItemId, precondition.Required);

        return NoContentOrProblem(
            Existing(precondition, await completeTodoItem.ExecuteAsync(command, cancellationToken)));
    }

    /// <summary>Removes an item from a todo list.</summary>
    [HttpDelete("{todoListId:guid}/items/{todoItemId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> RemoveItem(
        Guid todoListId,
        Guid todoItemId,
        CancellationToken cancellationToken)
    {
        var precondition = IfMatchPrecondition.Read(Request);

        if (Refused(precondition) is { } refusal)
        {
            return refusal;
        }

        var command = new RemoveTodoItemCommand(todoListId, todoItemId, precondition.Required);

        return NoContentOrProblem(
            Existing(precondition, await removeTodoItem.ExecuteAsync(command, cancellationToken)));
    }

    /// <summary>
    /// Publishes the version as an <c>ETag</c> and answers 304 when the caller already holds it.
    /// </summary>
    /// <remarks>
    /// The header is written before the status is chosen, because RFC 9110 requires a 304 to carry
    /// the validator it is refusing to resend the body for.
    /// </remarks>
    private ActionResult<TValue> Validated<TValue>(Result<Versioned<TValue>> result)
    {
        if (result.IsFailure)
        {
            return result.Error!.ToActionResult();
        }

        string tag = EntityTagValue.From(result.Value.Version);
        Response.Headers.ETag = tag;

        return IfNoneMatchPrecondition.Matches(Request, tag)
            ? StatusCode(StatusCodes.Status304NotModified)
            : Ok(result.Value.Value);
    }

    /// <summary>
    /// The transport-level verdicts on the header itself: 400 for a value that is not an
    /// <c>If-Match</c> at all, 428 where an unconditional write is refused by configuration.
    /// </summary>
    /// <returns><c>null</c> when the request may proceed.</returns>
    private ActionResult? Refused(IfMatchPrecondition precondition) => precondition.State switch
    {
        IfMatchState.Malformed => TodoListErrors.MalformedIfMatch.ToActionResult(),
        IfMatchState.Absent when concurrency.Value.IfMatch == IfMatchRequirement.Required =>
            TodoListErrors.IfMatchRequired.ToActionResult(),
        _ => null,
    };

    /// <summary>
    /// <c>If-Match: *</c> asserts that the resource exists, so its absence is that condition failing
    /// rather than a plain 404. Not distinguishing "no such list" from "somebody else's" survives
    /// the change, because both arrive here as the same not-found error.
    /// </summary>
    private static Result Existing(IfMatchPrecondition precondition, Result result) =>
        FailsExistence(precondition, result) ? Result.Failure(TodoListErrors.PreconditionFailed) : result;

    private static Result<TValue> Existing<TValue>(IfMatchPrecondition precondition, Result<TValue> result) =>
        FailsExistence(precondition, result)
            ? Result.Failure<TValue>(TodoListErrors.PreconditionFailed)
            : result;

    private static bool FailsExistence(IfMatchPrecondition precondition, Result result) =>
        precondition.State == IfMatchState.Any && result.Error?.Type == ErrorType.NotFound;
}

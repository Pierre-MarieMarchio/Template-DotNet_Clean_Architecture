using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.Common.Idempotency;
using AppTemplate.Api.Features.Reminders.Contracts.Requests;
using AppTemplate.Api.Features.Reminders.Contracts.Responses;
using AppTemplate.Api.Features.Reminders.Mapping;
using AppTemplate.Application.Features.Reminders.UseCases.Commands.CancelReminder;
using AppTemplate.Application.Features.Reminders.UseCases.Commands.RescheduleReminder;
using AppTemplate.Application.Features.Reminders.UseCases.Commands.ScheduleReminder;
using AppTemplate.Application.Features.Reminders.UseCases.Queries.GetReminders;
using Microsoft.AspNetCore.Mvc;

namespace AppTemplate.Api.Features.Reminders.Controllers;

/// <summary>
/// The reminder aggregate's HTTP surface.
/// </summary>
/// <remarks>
/// Authorisation is not declared here for the same reason as <c>TodoListsController</c>:
/// <c>Program.cs</c> installs a fallback policy requiring an authenticated user.
/// <para>
/// <b>Two addressing schemes, not one.</b> Scheduling and listing are reached through the item a
/// reminder is about, because <see cref="ScheduleReminderCommand"/> and
/// <see cref="GetRemindersQuery"/> both need that context. Rescheduling and cancelling are reached
/// by the reminder's own id alone, because <see cref="RescheduleReminderCommand"/> and
/// <see cref="CancelReminderCommand"/> name only <c>ReminderId</c> — a reminder is its own
/// aggregate root (<c>docs/adr/0024</c>), addressed independently of the list or item it is about,
/// unlike a <c>TodoItem</c> which is reachable only through its list.
/// </para>
/// <para>
/// <b>Conditional requests.</b> Scheduling and rescheduling answer with the reminder's own version
/// as a strong <c>ETag</c>; rescheduling and cancelling honour <c>If-Match</c> against it, decoded
/// by <c>ApiControllerBase.ReadPrecondition</c> the same way as every other feature. Scheduling
/// itself takes no precondition — <see cref="ScheduleReminderCommand"/> carries none, the same as
/// creating any resource that does not yet exist to have a version. Listing carries no
/// <c>ETag</c> either: <see cref="GetRemindersQuery"/> answers a plain list with no per-reminder
/// version attached, so there is nothing for <c>If-Match</c> to compare against.
/// </para>
/// <para>
/// <b>No single-reminder <c>GET</c>.</b> The application layer exposes only a query scoped to a
/// <c>TodoItemId</c>, never one reminder by its own id, so a created reminder's <c>Location</c>
/// points at the collection it now appears in rather than at a resource this surface has no other
/// way to address. The response body already carries the full representation and its <c>ETag</c>,
/// so a caller loses nothing by that.
/// </para>
/// </remarks>
[Route("api/v{version:apiVersion}")]
[Asp.Versioning.ApiVersion("1.0")]
[ProducesResponseType(StatusCodes.Status401Unauthorized, Type = typeof(ProblemDetails))]
public sealed class RemindersController(
    IGetRemindersUseCase getReminders,
    IScheduleReminderUseCase scheduleReminder,
    IRescheduleReminderUseCase rescheduleReminder,
    ICancelReminderUseCase cancelReminder) : ApiControllerBase
{
    /// <summary>Gets every reminder scheduled for one item.</summary>
    [HttpGet("todo-lists/{todoListId:guid}/items/{todoItemId:guid}/reminders", Name = nameof(GetForItem))]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RemindersResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    public async Task<ActionResult<RemindersResponse>> GetForItem(
        Guid todoListId,
        Guid todoItemId,
        CancellationToken cancellationToken)
    {
        var query = new GetRemindersQuery(todoListId, todoItemId);

        return OkOrProblem(ReminderResponseMapping.ToRemindersResponse(await getReminders.ExecuteAsync(query, cancellationToken)));
    }

    /// <summary>Schedules a reminder for one item.</summary>
    /// <remarks>
    /// Idempotent: send an <c>Idempotency-Key</c> header to make a retried request safe, on the
    /// same terms as <c>TodoListsController.Create</c>.
    /// </remarks>
    [HttpPost("todo-lists/{todoListId:guid}/items/{todoItemId:guid}/reminders")]
    [Idempotent]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(ReminderResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<ReminderResponse>> Schedule(
        Guid todoListId,
        Guid todoItemId,
        [FromBody] ScheduleReminderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new ScheduleReminderCommand(todoListId, todoItemId, request.DueAt);
        var result = ReminderResponseMapping.ToReminderResponse(await scheduleReminder.ExecuteAsync(command, cancellationToken));

        // No get-by-id to name (see the class remarks): Location addresses the collection this
        // reminder now appears in.
        return CreatedOrProblem(result, nameof(GetForItem), _ => new { todoListId, todoItemId });
    }

    /// <summary>Reschedules a reminder to a new due date.</summary>
    [HttpPut("reminders/{reminderId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ReminderResponse))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired, Type = typeof(ProblemDetails))]
    public async Task<ActionResult<ReminderResponse>> Reschedule(
        Guid reminderId,
        [FromBody] RescheduleReminderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (ReadPrecondition(out var precondition, out bool requiresExistence) is { } refusal)
        {
            return refusal;
        }

        var command = new RescheduleReminderCommand(reminderId, request.DueAt, precondition);
        var result = RequiringExistence(
            requiresExistence,
            await rescheduleReminder.ExecuteAsync(command, cancellationToken));

        return UpdatedOrProblem(ReminderResponseMapping.ToReminderResponse(result));
    }

    /// <summary>Cancels a reminder before it fires.</summary>
    /// <remarks>
    /// <c>DELETE</c>, not <c>POST .../cancel</c>: cancelling is a one-way, terminal transition —
    /// the same shape as <c>DeleteTodoList</c> — and needs no request body, unlike a <c>POST</c>
    /// that would have to carry an empty one.
    /// </remarks>
    [HttpDelete("reminders/{reminderId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(ValidationProblemDetails))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status409Conflict, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed, Type = typeof(ProblemDetails))]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired, Type = typeof(ProblemDetails))]
    public async Task<ActionResult> Cancel(Guid reminderId, CancellationToken cancellationToken)
    {
        if (ReadPrecondition(out var precondition, out bool requiresExistence) is { } refusal)
        {
            return refusal;
        }

        var command = new CancelReminderCommand(reminderId, precondition);
        var result = RequiringExistence(requiresExistence, await cancelReminder.ExecuteAsync(command, cancellationToken));

        return NoContentOrProblem(result);
    }
}

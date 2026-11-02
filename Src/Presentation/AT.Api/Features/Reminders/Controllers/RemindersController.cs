using AppTemplate.Api.Common.Controllers;
using AppTemplate.Api.Common.Idempotency;
using AppTemplate.Api.Features.Reminders.Contracts.Mapping;
using AppTemplate.Api.Features.Reminders.Contracts.Requests;
using AppTemplate.Api.Features.Reminders.Contracts.Responses;
using AppTemplate.Application.Features.Reminders.UseCases.Commands.CancelReminder;
using AppTemplate.Application.Features.Reminders.UseCases.Commands.RescheduleReminder;
using AppTemplate.Application.Features.Reminders.UseCases.Commands.ScheduleReminder;
using AppTemplate.Application.Features.Reminders.UseCases.Queries.GetReminders;
using Microsoft.AspNetCore.Mvc;

namespace AppTemplate.Api.Features.Reminders.Controllers;

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

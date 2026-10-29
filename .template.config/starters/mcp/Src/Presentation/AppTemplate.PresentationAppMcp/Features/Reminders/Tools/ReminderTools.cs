using System.ComponentModel;
using AppTemplate.Application.Features.Reminders.UseCases.Commands.ScheduleReminder;
using AppTemplate.Application.Features.Reminders.UseCases.Queries.GetReminders;
using AppTemplate.PresentationAppMcp.Common.Security;
using AppTemplate.PresentationAppMcp.Common.Tools;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AppTemplate.PresentationAppMcp.Features.Reminders.Tools;

[McpServerToolType]
internal sealed class ReminderTools(
    IGetRemindersUseCase getReminders,
    IScheduleReminderUseCase scheduleReminder)
{
    [McpServerTool(Name = "list_reminders", Title = "List an item's reminders", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Authorize(Policy = ToolPolicies.Read)]
    [Description("Lists the reminders scheduled on one item of one of the caller's to-do lists.")]
    public async Task<CallToolResult> ListRemindersAsync(
        [Description("The list's id, as list_todo_lists gives it.")] Guid todoListId,
        [Description("The item's id, as get_todo_list gives it.")] Guid todoItemId,
        CancellationToken cancellationToken = default) =>
        ToolAnswer.From(
            await getReminders.ExecuteAsync(new GetRemindersQuery(todoListId, todoItemId), cancellationToken),
            reminders => new { reminders });

    [McpServerTool(Name = "schedule_reminder", Title = "Schedule a reminder", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Authorize(Policy = ToolPolicies.Write)]
    [Description("Schedules a reminder, sent by email to the caller when it falls due, on one item of one of the caller's to-do lists.")]
    public async Task<CallToolResult> ScheduleReminderAsync(
        [Description("The list's id, as list_todo_lists gives it.")] Guid todoListId,
        [Description("The item's id, as get_todo_list gives it.")] Guid todoItemId,
        [Description("When the reminder falls due, an ISO 8601 date and time with its offset, in the future.")] DateTimeOffset dueAt,
        CancellationToken cancellationToken = default) =>
        ToolAnswer.From(
            await scheduleReminder.ExecuteAsync(new ScheduleReminderCommand(todoListId, todoItemId, dueAt), cancellationToken),
            reminder => reminder.Value);
}

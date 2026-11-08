using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Features.Reminders.Model.Dtos;
using AppTemplate.Application.Features.Reminders.UseCases.Commands.ScheduleReminder;
using AppTemplate.Application.Features.Reminders.UseCases.Queries.GetReminders;
using AppTemplate.Domain.Features.Reminders.Model.ValueObjects;
using AppTemplate.PresentationAppMcp.Features.Reminders.Tools;
using Mediator;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.PresentationAppMcp.UnitTests.Features.Reminders.Tools;

public sealed class ReminderToolsTests
{
    private static readonly ReminderDto _reminder = new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        DateTimeOffset.UnixEpoch.AddYears(60),
        ReminderState.Pending,
        null);

    private readonly ISender _sender = Substitute.For<ISender>();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private ReminderTools Tools => new(_sender);

    [Fact]
    public async Task ListReminders_AnswersTheItemsReminders_InAnObject()
    {
        _sender.Send(new GetRemindersQuery(_reminder.TodoListId, _reminder.TodoItemId), Arg.Any<CancellationToken>())
            .Returns(Result.Success<IReadOnlyList<ReminderDto>>([_reminder]));

        var answer = await Tools.ListRemindersAsync(_reminder.TodoListId, _reminder.TodoItemId, Cancellation);

        var reminder = answer.StructuredContent!.Value.GetProperty("reminders")[0];
        reminder.GetProperty("id").GetGuid().ShouldBe(_reminder.Id);
        reminder.GetProperty("state").GetString().ShouldBe("pending");
    }

    [Fact]
    public async Task ScheduleReminder_SchedulesTheReminderItWasGiven()
    {
        var command = new ScheduleReminderCommand(_reminder.TodoListId, _reminder.TodoItemId, _reminder.DueAt);
        _sender.Send(command, Arg.Any<CancellationToken>())
            .Returns(Result.Success(new Versioned<ReminderDto>(_reminder, 1)));

        var answer = await Tools.ScheduleReminderAsync(command.TodoListId, command.TodoItemId, command.DueAt, Cancellation);

        answer.StructuredContent!.Value.GetProperty("dueAt").GetDateTimeOffset().ShouldBe(_reminder.DueAt);
    }
}

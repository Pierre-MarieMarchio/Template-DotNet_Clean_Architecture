using AppTemplate.Application.Features.Reminders.Mapping;
using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Common.Abstractions;
using AppTemplate.Domain.Features.Reminders.ValueObjects;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Reminders.Mapping;

public sealed class ReminderDtoMappingTests
{
    private static readonly Guid _ownerId = Guid.CreateVersion7();

    [Fact]
    public void ToDto_MapsEveryField()
    {
        var reminder = AReminder.OwnedBy(_ownerId);

        var dto = ReminderDtoMapping.ToDto(reminder);

        dto.Id.ShouldBe(reminder.Id);
        dto.TodoListId.ShouldBe(reminder.TodoListId);
        dto.TodoItemId.ShouldBe(reminder.TodoItemId);
        dto.DueAt.ShouldBe(reminder.DueAt);
        dto.State.ShouldBe(ReminderState.Pending);
        dto.ClaimedAt.ShouldBeNull();
        dto.NotifiedAt.ShouldBeNull();
    }

    [Fact]
    public void ToVersioned_CarriesTheAggregatesOwnVersion()
    {
        var reminder = AReminder.OwnedBy(_ownerId);
        ((IVersioned)reminder).SetVersion(9);

        var projected = ReminderDtoMapping.ToVersioned(reminder);

        projected.Version.ShouldBe(9u);
        projected.Value.Id.ShouldBe(reminder.Id);
    }

    [Fact]
    public void ToDto_Rejects_ANullReminder() =>
        Should.Throw<ArgumentNullException>(() => ReminderDtoMapping.ToDto(null!));
}

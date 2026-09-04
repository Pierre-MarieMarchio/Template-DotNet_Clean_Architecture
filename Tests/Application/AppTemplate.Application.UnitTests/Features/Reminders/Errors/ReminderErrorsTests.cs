using AppTemplate.Application.Common;
using AppTemplate.Application.Features.Reminders.Errors;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Reminders.Errors;

public sealed class ReminderErrorsTests
{
    [Fact]
    public void ReminderNotFound_IsANotFoundErrorNamingTheReminder()
    {
        var reminderId = Guid.CreateVersion7();

        var error = ReminderErrors.ReminderNotFound(reminderId);

        error.Type.ShouldBe(ErrorType.NotFound);
        error.Code.ShouldBe("reminder.notFound");
        error.Message.ShouldContain(reminderId.ToString());
    }

    [Fact]
    public void TargetNotFound_IsANotFoundErrorNamingTheItem()
    {
        var itemId = Guid.CreateVersion7();

        var error = ReminderErrors.TargetNotFound(itemId);

        error.Type.ShouldBe(ErrorType.NotFound);
        error.Code.ShouldBe("reminder.targetNotFound");
        error.Message.ShouldContain(itemId.ToString());
    }

    /// <summary>Clients branch on the code rather than on the prose, so two must never collide.</summary>
    [Fact]
    public void EveryCode_IsDistinct()
    {
        string[] codes =
        [
            ReminderErrors.ReminderNotFound(Guid.Empty).Code,
            ReminderErrors.TargetNotFound(Guid.Empty).Code,
        ];

        codes.Distinct(StringComparer.Ordinal).Count().ShouldBe(codes.Length);
        codes.ShouldAllBe(code => code.Contains('.', StringComparison.Ordinal));
    }
}

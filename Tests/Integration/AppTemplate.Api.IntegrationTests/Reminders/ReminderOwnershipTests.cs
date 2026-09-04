using System.Net;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Reminders;

/// <summary>
/// A reminder belonging to somebody else must be indistinguishable from one that does not exist at
/// all. <c>ReminderErrors.ReminderNotFound</c>'s own remarks say why: a 403 would itself reveal that
/// the id names something real, which is exactly the information this feature must not leak. That
/// rule holds across the repository, not only for <c>TodoLists</c> — these tests are what prove it
/// for the reminder aggregate too.
/// </summary>
public sealed class ReminderOwnershipTests(ApiFixture fixture) : RemindersTestBase(fixture)
{
    [Fact]
    public async Task ReschedulingSomebodyElsesReminder_Is404NotAForbidden()
    {
        var (owner, _, _) = await SignInAsync("owner");
        var listId = await CreateTodoListAsync(owner, "Owner's list");
        var itemId = await AddTodoItemAsync(owner, listId, "Owner's item");
        var reminder = await ScheduleReminderAsync(owner, listId, itemId, Clock.UtcNow.AddDays(1));

        var (stranger, _, _) = await SignInAsync("stranger");

        using var response = await RescheduleAsync(stranger, reminder.Id, Clock.UtcNow.AddDays(2));

        response.StatusCode.ShouldBe(
            HttpStatusCode.NotFound,
            "a reminder that belongs to somebody else must answer exactly like one that does not " +
            "exist, never like a 403 that would confirm it does: " +
            await response.Content.ReadAsStringAsync(TestToken));

        var problem = await ApiJson.ReadProblemAsync(response, TestToken);
        problem.Code.ShouldBe("reminder.notFound", problem.Body);

        // The same shape a genuinely unknown id produces — same status, same title, same code — so a
        // caller cannot tell "exists, but not yours" apart from "never existed" by anything but the
        // id it already supplied itself.
        using var unknown = await RescheduleAsync(stranger, Guid.CreateVersion7(), Clock.UtcNow.AddDays(2));
        var unknownProblem = await ApiJson.ReadProblemAsync(unknown, TestToken);

        problem.Status.ShouldBe(unknownProblem.Status);
        problem.Title.ShouldBe(unknownProblem.Title);
        problem.Code.ShouldBe(unknownProblem.Code);

        // And untouched: the stranger's refused attempt changed nothing about the owner's reminder.
        (await GetRemindersForItemAsync(owner, listId, itemId)).Reminders.Single().DueAt.ShouldBe(reminder.DueAt);
    }

    [Fact]
    public async Task CancellingSomebodyElsesReminder_Is404NotAForbidden()
    {
        var (owner, _, _) = await SignInAsync("owner");
        var listId = await CreateTodoListAsync(owner, "Owner's list");
        var itemId = await AddTodoItemAsync(owner, listId, "Owner's item");
        var reminder = await ScheduleReminderAsync(owner, listId, itemId, Clock.UtcNow.AddDays(1));

        var (stranger, _, _) = await SignInAsync("stranger");

        using var response = await CancelAsync(stranger, reminder.Id);

        response.StatusCode.ShouldBe(
            HttpStatusCode.NotFound,
            "cancelling somebody else's reminder must not be distinguishable from cancelling one " +
            "that does not exist: " + await response.Content.ReadAsStringAsync(TestToken));

        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("reminder.notFound");

        // Still pending: the stranger's refused attempt left it exactly where the owner left it.
        (await GetRemindersForItemAsync(owner, listId, itemId)).Reminders.Single().Status.ShouldBe("pending");
    }

    /// <summary>
    /// Listing is scoped to the caller too, even though the route names an item rather than a
    /// reminder directly: <c>GetRemindersUseCase</c> filters by owner rather than trusting the item
    /// id in the route to mean the caller may see everything scheduled against it.
    /// </summary>
    [Fact]
    public async Task ListingReminders_NeverIncludesSomebodyElsesEvenWhenAskedForTheSameItemId()
    {
        var (owner, _, _) = await SignInAsync("owner");
        var listId = await CreateTodoListAsync(owner, "Owner's list");
        var itemId = await AddTodoItemAsync(owner, listId, "Owner's item");
        await ScheduleReminderAsync(owner, listId, itemId, Clock.UtcNow.AddDays(1));

        var (stranger, _, _) = await SignInAsync("stranger");

        var strangersView = await GetRemindersForItemAsync(stranger, listId, itemId);

        strangersView.Reminders.ShouldBeEmpty();
    }
}

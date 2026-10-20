using System.Net;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Reminders;

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

        using var unknown = await RescheduleAsync(stranger, Guid.CreateVersion7(), Clock.UtcNow.AddDays(2));
        var unknownProblem = await ApiJson.ReadProblemAsync(unknown, TestToken);

        problem.Status.ShouldBe(unknownProblem.Status);
        problem.Title.ShouldBe(unknownProblem.Title);
        problem.Code.ShouldBe(unknownProblem.Code);

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

        (await GetRemindersForItemAsync(owner, listId, itemId)).Reminders.Single().Status.ShouldBe("pending");
    }

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

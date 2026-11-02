using System.Net;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Reminders;

public sealed class FireDueRemindersTests(ApiFixture fixture) : RemindersTestBase(fixture)
{
    [Fact]
    public async Task ADueReminder_FiresAndRecordsExactlyOneNotification()
    {
        Notifications.Clear();

        var (client, _, session) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Chores");
        var itemId = await AddTodoItemAsync(client, listId, "Water the plants");

        var reminder = await ScheduleReminderAsync(client, listId, itemId, Clock.UtcNow.AddMinutes(5));
        var dueAt = Clock.UtcNow.AddMinutes(-1);
        await BackdateReminderAsync(reminder.Id, dueAt);

        int notified = await FireDueRemindersAsync();

        notified.ShouldBe(1);

        var sent = Notifications.LastFor(itemId);
        sent.ShouldNotBeNull();
        sent!.OwnerId.Value.ShouldBe(session.UserId);
        sent.TodoItemId.ShouldBe(itemId);
        sent.DueAt.ShouldBe(dueAt);

        var fired = (await GetRemindersForItemAsync(client, listId, itemId)).Reminders.Single();
        fired.Status.ShouldBe("fired");
        fired.NotifiedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task AReminderAlreadyFired_DoesNotFireAgainOnTheNextPass()
    {
        Notifications.Clear();

        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Chores");
        var itemId = await AddTodoItemAsync(client, listId, "Water the plants");

        var reminder = await ScheduleReminderAsync(client, listId, itemId, Clock.UtcNow.AddMinutes(5));
        await BackdateReminderAsync(reminder.Id, Clock.UtcNow.AddMinutes(-1));

        (await FireDueRemindersAsync()).ShouldBe(1);
        Notifications.Snapshot().Count.ShouldBe(1);

        int notifiedAgain = await FireDueRemindersAsync();

        notifiedAgain.ShouldBe(0);
        Notifications.Snapshot().Count.ShouldBe(
            1,
            "a Fired reminder is terminal: GetDueAsync only ever returns Pending rows, so a second " +
            "pass must not notify it again.");

        (await GetRemindersForItemAsync(client, listId, itemId)).Reminders.Single().Status.ShouldBe("fired");
    }

    [Fact]
    public async Task AReminderWaitingOutAFailedAttempt_IsNotFiredBeforeItsRetryTime()
    {
        Notifications.Clear();

        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Chores");
        var itemId = await AddTodoItemAsync(client, listId, "Water the plants");

        var reminder = await ScheduleReminderAsync(client, listId, itemId, Clock.UtcNow.AddMinutes(5));
        await BackdateReminderAsync(reminder.Id, Clock.UtcNow.AddMinutes(-30));
        await PutInBackoffAsync(reminder.Id, Clock.UtcNow.AddMinutes(10));

        (await FireDueRemindersAsync()).ShouldBe(0, "its retry time is still ahead");
        Notifications.Snapshot().ShouldBeEmpty();
        (await GetRemindersForItemAsync(client, listId, itemId)).Reminders.Single().Status.ShouldBe("pending");

        await PutInBackoffAsync(reminder.Id, Clock.UtcNow.AddMinutes(-1));

        (await FireDueRemindersAsync()).ShouldBe(1, "its retry time has passed");
        (await GetRemindersForItemAsync(client, listId, itemId)).Reminders.Single().Status.ShouldBe("fired");
    }

    [Fact]
    public async Task AnItemCompletedThroughTheNormalEndpoint_CancelsItsReminderAndFiringNeverNotifies()
    {
        Notifications.Clear();

        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Chores");
        var itemId = await AddTodoItemAsync(client, listId, "Water the plants");

        var reminder = await ScheduleReminderAsync(client, listId, itemId, Clock.UtcNow.AddMinutes(5));

        using var completed = await client.PostAsync(
            new Uri($"{TodoListsRoute}/{listId}/items/{itemId}/complete", UriKind.Relative),
            content: null,
            TestToken);
        completed.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await GetRemindersForItemAsync(client, listId, itemId)).Reminders.Single().Status.ShouldBe("cancelled");

        await BackdateReminderAsync(reminder.Id, Clock.UtcNow.AddMinutes(-1));

        int notified = await FireDueRemindersAsync();

        notified.ShouldBe(0);
        Notifications.Snapshot().ShouldBeEmpty();
    }

    [Fact]
    public async Task AnItemCompletedWithoutTheEventEverFiring_StillDoesNotGetNotified()
    {
        Notifications.Clear();

        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Chores");
        var itemId = await AddTodoItemAsync(client, listId, "Water the plants");

        var reminder = await ScheduleReminderAsync(client, listId, itemId, Clock.UtcNow.AddMinutes(5));
        await BackdateReminderAsync(reminder.Id, Clock.UtcNow.AddMinutes(-1));

        await CompleteTodoItemWithoutRaisingAnEventAsync(itemId, Clock.UtcNow);

        (await GetRemindersForItemAsync(client, listId, itemId)).Reminders.Single().Status.ShouldBe("pending");

        int notified = await FireDueRemindersAsync();

        notified.ShouldBe(
            0,
            "firing must re-check the target's completion itself rather than trust a cancellation " +
            "event that never arrived.");
        Notifications.Snapshot().ShouldBeEmpty();

        var settled = (await GetRemindersForItemAsync(client, listId, itemId)).Reminders.Single();
        settled.Status.ShouldBe("cancelled");
        settled.NotifiedAt.ShouldBeNull();
    }

    [Fact]
    public async Task ARemovedItemsReminder_IsCancelledWithoutEverNotifying()
    {
        Notifications.Clear();

        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Chores");
        var itemId = await AddTodoItemAsync(client, listId, "Water the plants");

        var reminder = await ScheduleReminderAsync(client, listId, itemId, Clock.UtcNow.AddMinutes(5));
        await BackdateReminderAsync(reminder.Id, Clock.UtcNow.AddMinutes(-1));

        using var removed = await client.DeleteAsync(
            new Uri($"{TodoListsRoute}/{listId}/items/{itemId}", UriKind.Relative),
            TestToken);
        removed.StatusCode.ShouldBe(HttpStatusCode.OK);

        int notified = await FireDueRemindersAsync();

        notified.ShouldBe(0);
        Notifications.Snapshot().ShouldBeEmpty();

        (await GetRemindersForItemAsync(client, listId, itemId)).Reminders.Single().Status.ShouldBe("cancelled");
    }
}

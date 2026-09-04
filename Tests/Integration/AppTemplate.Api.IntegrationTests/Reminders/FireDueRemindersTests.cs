using System.Net;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Reminders;

/// <summary>
/// The guarantees <c>FireDueRemindersUseCase</c>'s own XML comment states, proved end to end through
/// the real routes and the real store rather than through a mock of any of its ports: a due reminder
/// fires exactly once, a fired reminder never fires again, and — the reason the use case re-checks
/// completion at all instead of trusting the event that should have already cancelled a reminder —
/// a target that turns out to be already completed or altogether gone is cancelled instead of
/// notified, whether or not the event that was supposed to retire it first ever arrived.
/// </summary>
public sealed class FireDueRemindersTests(ApiFixture fixture) : RemindersTestBase(fixture)
{
    [Fact]
    public async Task ADueReminder_FiresAndRecordsExactlyOneNotification()
    {
        // The recording double is a singleton for the whole suite (see RemindersTestBase's own
        // remarks) and nothing clears it automatically, so a test that reads it clears it itself.
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
        sent!.OwnerId.ShouldBe(session.UserId);
        sent.TodoItemId.ShouldBe(itemId);
        sent.DueAt.ShouldBe(dueAt);

        var fired = (await GetRemindersForItemAsync(client, listId, itemId)).Reminders.Single();
        fired.Status.ShouldBe("fired");
        fired.NotifiedAt.ShouldNotBeNull();
        fired.ClaimedAt.ShouldNotBeNull();
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

        // A second pass over the same batch — exactly what a crash-and-retry, or the worker's own
        // next tick, would also see.
        int notifiedAgain = await FireDueRemindersAsync();

        notifiedAgain.ShouldBe(0);
        Notifications.Snapshot().Count.ShouldBe(
            1,
            "a Fired reminder is terminal: GetDueAsync only ever returns Pending rows, so a second " +
            "pass must not notify it again.");

        (await GetRemindersForItemAsync(client, listId, itemId)).Reminders.Single().Status.ShouldBe("fired");
    }

    /// <summary>
    /// The fast path working as intended: a completion that goes through the ordinary endpoint
    /// retires its reminder through <c>CancelRemindersOnTodoItemCompletedConsumer</c> before the
    /// reminder is ever due, and firing later finds nothing pending to notify.
    /// </summary>
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

        // Cancelled already, before the reminder ever comes due — the consumer's own job, proved
        // independently of firing.
        (await GetRemindersForItemAsync(client, listId, itemId)).Reminders.Single().Status.ShouldBe("cancelled");

        await BackdateReminderAsync(reminder.Id, Clock.UtcNow.AddMinutes(-1));

        int notified = await FireDueRemindersAsync();

        notified.ShouldBe(0);
        Notifications.Snapshot().ShouldBeEmpty();
    }

    /// <summary>
    /// The scenario the whole feature exists for. <c>CancelRemindersOnTodoItemCompletedConsumer</c>
    /// is a best-effort fast path, not the correctness guarantee (see its own remarks): here the item
    /// is completed by writing the column directly, exactly as it would be left by a completion whose
    /// domain event never reached that consumer. Firing must still refuse to notify, because it
    /// re-checks the target's completion itself rather than trusting a cancellation that never
    /// happened.
    /// </summary>
    [Fact]
    public async Task AnItemCompletedWithoutTheEventEverFiring_StillDoesNotGetNotified()
    {
        Notifications.Clear();

        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Chores");
        var itemId = await AddTodoItemAsync(client, listId, "Water the plants");

        var reminder = await ScheduleReminderAsync(client, listId, itemId, Clock.UtcNow.AddMinutes(5));
        await BackdateReminderAsync(reminder.Id, Clock.UtcNow.AddMinutes(-1));

        // The lost event: completed, but not through the use case that would have raised it.
        await CompleteTodoItemWithoutRaisingAnEventAsync(itemId, Clock.UtcNow);

        // Proof the simulation actually landed: the fast-path consumer never ran, so the reminder is
        // still Pending going into the firing pass below.
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

    /// <summary>
    /// An item removed outright raises no domain event either — there is nothing for the consumer to
    /// have missed — and firing treats an id absent from the target lookup exactly like a completed
    /// one: cancelled, never notified.
    /// </summary>
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

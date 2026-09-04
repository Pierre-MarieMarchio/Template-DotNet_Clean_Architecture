using System.Net;
using System.Net.Http.Json;
using AppTemplate.Api.Features.Reminders.Contracts.Requests;
using AppTemplate.Api.Features.Reminders.Contracts.Responses;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Reminders;

/// <summary>
/// The reminder aggregate's whole HTTP surface, in the order a caller actually uses it: schedule,
/// read, reschedule, cancel. Conditional writes are checked the same way <c>TodoLists</c>'s own
/// tests check them — a stale <c>If-Match</c> refused, the current one accepted — because
/// <c>RemindersController</c> reuses the very same precondition mechanism.
/// </summary>
public sealed class ReminderLifecycleTests(ApiFixture fixture) : RemindersTestBase(fixture)
{
    [Fact]
    public async Task SchedulingReadingReschedulingAndCancelling_FollowsTheFullHttpLifecycle()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Chores");
        var itemId = await AddTodoItemAsync(client, listId, "Water the plants");

        var dueAt = Clock.UtcNow.AddDays(1);

        using var scheduled = await client.PostAsJsonAsync(
            $"{TodoListsRoute}/{listId}/items/{itemId}/reminders",
            new ScheduleReminderRequest(dueAt),
            TestToken);

        scheduled.StatusCode.ShouldBe(HttpStatusCode.Created);
        scheduled.Headers.ETag.ShouldNotBeNull();
        scheduled.Headers.Location.ShouldNotBeNull();

        // No get-by-id for one reminder (see RemindersController's own remarks): Location addresses
        // the collection this reminder now appears in.
        scheduled.Headers.Location!.ToString().ShouldEndWith($"{TodoListsRoute}/{listId}/items/{itemId}/reminders");

        var created = await ApiJson.ReadAsync<ReminderResponse>(scheduled, TestToken);
        created.TodoListId.ShouldBe(listId);
        created.TodoItemId.ShouldBe(itemId);
        created.DueAt.ShouldBe(dueAt);
        created.Status.ShouldBe("pending");
        created.ClaimedAt.ShouldBeNull();
        created.NotifiedAt.ShouldBeNull();

        string etagAfterScheduling = scheduled.Headers.ETag!.ToString();

        // Following the Location header reaches it.
        using var located = await client.GetAsync(scheduled.Headers.Location, TestToken);
        located.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ApiJson.ReadAsync<RemindersResponse>(located, TestToken)).Reminders
            .ShouldHaveSingleItem().Id.ShouldBe(created.Id);

        // Reading it back through the item, the ordinary way.
        (await GetRemindersForItemAsync(client, listId, itemId)).Reminders
            .ShouldHaveSingleItem().Id.ShouldBe(created.Id);

        // Rescheduling with a stale validator is refused, and nothing about the reminder moves.
        var newDueAt = dueAt.AddDays(1);

        using var staleReschedule = await RescheduleAsync(client, created.Id, newDueAt, "\"not-the-current-one\"");
        staleReschedule.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);
        (await ApiJson.ReadProblemAsync(staleReschedule, TestToken)).Code.ShouldBe("precondition.failed");

        (await GetRemindersForItemAsync(client, listId, itemId)).Reminders.Single().DueAt.ShouldBe(dueAt);

        // Rescheduling with the reminder's own current validator succeeds and publishes a new one.
        using var rescheduled = await RescheduleAsync(client, created.Id, newDueAt, etagAfterScheduling);
        rescheduled.StatusCode.ShouldBe(HttpStatusCode.OK);
        rescheduled.Headers.ETag.ShouldNotBeNull();
        rescheduled.Headers.ETag!.ToString().ShouldNotBe(
            etagAfterScheduling,
            "a validator that survived a write would let the next stale request through.");

        var afterReschedule = await ApiJson.ReadAsync<ReminderResponse>(rescheduled, TestToken);
        afterReschedule.DueAt.ShouldBe(newDueAt);
        afterReschedule.Status.ShouldBe("pending");

        string etagAfterReschedule = rescheduled.Headers.ETag!.ToString();

        // Cancelling with the validator from before the reschedule is now stale too.
        using var staleCancel = await CancelAsync(client, created.Id, etagAfterScheduling);
        staleCancel.StatusCode.ShouldBe(HttpStatusCode.PreconditionFailed);

        (await GetRemindersForItemAsync(client, listId, itemId)).Reminders.Single().Status.ShouldBe("pending");

        // Cancelling with the current validator succeeds and is terminal.
        using var cancelled = await CancelAsync(client, created.Id, etagAfterReschedule);
        cancelled.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await GetRemindersForItemAsync(client, listId, itemId)).Reminders.Single().Status.ShouldBe("cancelled");

        // A cancelled reminder cannot be rescheduled, unconditionally: the aggregate itself refuses,
        // independently of whatever validator is attached.
        using var rescheduleAfterCancel = await RescheduleAsync(client, created.Id, newDueAt.AddDays(1));
        rescheduleAfterCancel.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var refusal = await ApiJson.ReadProblemAsync(rescheduleAfterCancel, TestToken);
        refusal.Code.ShouldBe("domain.invariantViolated", refusal.Body);
    }

    /// <summary>
    /// Scheduling itself is unconditional — it addresses a reminder that does not exist yet, so
    /// there is no version an <c>If-Match</c> could name — the same reasoning as creating a to-do
    /// list.
    /// </summary>
    [Fact]
    public async Task Scheduling_NeedsNoIfMatchBecauseNothingExistsYetToNameAVersionOf()
    {
        var (client, _, _) = await SignInAsync();
        var listId = await CreateTodoListAsync(client, "Chores");
        var itemId = await AddTodoItemAsync(client, listId, "Water the plants");

        var reminder = await ScheduleReminderAsync(client, listId, itemId, Clock.UtcNow.AddDays(1));

        reminder.Status.ShouldBe("pending");
    }

    /// <summary>An unknown reminder id is a plain 404 without any <c>If-Match</c> attached.</summary>
    [Fact]
    public async Task WithoutAnIfMatch_AnUnknownReminderIsStillAPlainNotFound()
    {
        var (client, _, _) = await SignInAsync();

        using var response = await CancelAsync(client, Guid.CreateVersion7());

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ApiJson.ReadProblemAsync(response, TestToken)).Code.ShouldBe("reminder.notFound");
    }
}

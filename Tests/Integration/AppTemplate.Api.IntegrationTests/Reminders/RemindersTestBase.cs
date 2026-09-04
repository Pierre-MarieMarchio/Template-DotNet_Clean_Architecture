using System.Net;
using System.Net.Http.Json;
using AppTemplate.Api.Features.Reminders.Contracts.Requests;
using AppTemplate.Api.Features.Reminders.Contracts.Responses;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Application.Features.Reminders.UseCases.Commands.FireDueReminders;
using AppTemplate.Infrastructure.InMemory.Features.Reminders;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AppTemplate.Api.IntegrationTests.Reminders;

/// <summary>
/// What every reminder test shares: the second route (reminders are addressed by their own id, not
/// through the list — see <c>RemindersController</c>'s own remarks), and the two things no endpoint
/// exposes at all: moving a reminder's due date into the past, and completing a to-do item without
/// raising the domain event that would normally retire its reminder.
/// </summary>
/// <remarks>
/// <para>
/// <b>Backdating through the store, not through the clock.</b> Scheduling and rescheduling both
/// refuse a due date that is not in the future, so a reminder due <em>now</em> has to be created due
/// shortly and then moved into the past directly. Moving the clock instead was rejected on purpose:
/// <see cref="FireDueRemindersUseCase"/> reads the same frozen clock the bearer handler validates
/// tokens against, and setting it backwards after a token has already been minted risks an
/// <c>nbf</c> the handler refuses. Both direct writes go through <c>AppDbContext.Database</c> — a
/// public method on a public type — never through an internal persistence model, the same boundary
/// <see cref="TestDatabase"/> keeps for the same reason.
/// </para>
/// <para>
/// <b>Firing is driven directly, not through HTTP.</b> <see cref="IFireDueRemindersUseCase"/> has no
/// route: it runs from <c>AppTemplate.Worker</c> on a timer, never from a request. Resolving it from
/// the API host's own container and running it from a scope of the test's own is the same shape used
/// elsewhere in this suite for <c>IUnitOfWork</c> — the exact production type, composed the way a
/// real pass would compose it, just invoked once on demand instead of on a timer.
/// </para>
/// </remarks>
public abstract class RemindersTestBase(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    /// <summary>Reminders are their own aggregate, addressed by their own id — never nested under a
    /// list or an item the way a <c>TodoItem</c> is.</summary>
    protected const string RemindersRoute = "/api/v1/reminders";

    /// <summary>
    /// The recording double behind <c>IReminderNotifier</c>, resolved the same way
    /// <see cref="ApiFixture.Emails"/> resolves its own singleton. Nothing in
    /// <see cref="IntegrationTestBase"/> clears it between tests — it has no reason to know this
    /// double exists — so a test that reads it clears it itself, right before the action under test,
    /// the same way other tests in this suite clear <c>Fixture.DomainEvents</c> defensively even
    /// though the base already does for the events it does know about.
    /// </summary>
    protected RecordedReminderNotifications Notifications =>
        Fixture.Factory.Services.GetRequiredService<RecordedReminderNotifications>();

    protected static async Task<ReminderResponse> ScheduleReminderAsync(
        HttpClient client,
        Guid todoListId,
        Guid todoItemId,
        DateTimeOffset dueAt)
    {
        ArgumentNullException.ThrowIfNull(client);

        using var response = await client.PostAsJsonAsync(
            $"{TodoListsRoute}/{todoListId}/items/{todoItemId}/reminders",
            new ScheduleReminderRequest(dueAt),
            TestToken);

        if (response.StatusCode != HttpStatusCode.Created)
        {
            throw new InvalidOperationException(
                $"Scheduling a reminder for item '{todoItemId}' failed with {(int)response.StatusCode}: " +
                await response.Content.ReadAsStringAsync(TestToken));
        }

        return await ApiJson.ReadAsync<ReminderResponse>(response, TestToken);
    }

    protected static async Task<RemindersResponse> GetRemindersForItemAsync(
        HttpClient client,
        Guid todoListId,
        Guid todoItemId)
    {
        ArgumentNullException.ThrowIfNull(client);

        using var response = await client.GetAsync(
            new Uri($"{TodoListsRoute}/{todoListId}/items/{todoItemId}/reminders", UriKind.Relative),
            TestToken);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"Reading the reminders of item '{todoItemId}' failed with {(int)response.StatusCode}: " +
                await response.Content.ReadAsStringAsync(TestToken));
        }

        return await ApiJson.ReadAsync<RemindersResponse>(response, TestToken);
    }

    /// <summary>
    /// A reschedule carrying whatever the caller wants in <c>If-Match</c>, including nothing at all —
    /// the same shape as <c>IntegrationTestBase.RenameAsync</c>, for the same reason: a test that
    /// could only send values <see cref="HttpClient"/> considers well-formed could never exercise the
    /// malformed case.
    /// </summary>
    protected static Task<HttpResponseMessage> RescheduleAsync(
        HttpClient client,
        Guid reminderId,
        DateTimeOffset dueAt,
        string? ifMatch = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        var request = new HttpRequestMessage(HttpMethod.Put, $"{RemindersRoute}/{reminderId}")
        {
            Content = JsonContent.Create(new RescheduleReminderRequest(dueAt)),
        };

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return client.SendAsync(request, TestToken);
    }

    protected static Task<HttpResponseMessage> CancelAsync(HttpClient client, Guid reminderId, string? ifMatch = null)
    {
        ArgumentNullException.ThrowIfNull(client);

        var request = new HttpRequestMessage(HttpMethod.Delete, $"{RemindersRoute}/{reminderId}");

        if (ifMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        }

        return client.SendAsync(request, TestToken);
    }

    /// <summary>
    /// Moves a reminder's due date into the past, bypassing the refusal <c>Reschedule</c> would
    /// raise against one. Loading a stored reminder deliberately does not re-check that its due date
    /// is in the future — that is a precondition of scheduling, not a property of the stored state —
    /// which is what makes a row backdated this way loadable by the very query
    /// <see cref="IFireDueRemindersUseCase"/> runs.
    /// </summary>
    protected async Task BackdateReminderAsync(Guid reminderId, DateTimeOffset dueAt)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE reminders."Reminders" SET "DueAt" = {dueAt} WHERE "Id" = {reminderId}""",
            TestToken);
    }

    /// <summary>
    /// Completes a to-do item by writing the column directly, the way a completion that never went
    /// through the use case would leave it: <c>CompletedAt</c> set, but no
    /// <c>TodoItemCompletedDomainEvent</c> ever raised, and therefore
    /// <c>CancelRemindersOnTodoItemCompletedConsumer</c> never runs. This is the lost event
    /// <see cref="FireDueRemindersUseCase"/>'s own remarks describe re-checking completion against.
    /// </summary>
    protected async Task CompleteTodoItemWithoutRaisingAnEventAsync(Guid todoItemId, DateTimeOffset completedAt)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""UPDATE todo."TodoItems" SET "CompletedAt" = {completedAt} WHERE "Id" = {todoItemId}""",
            TestToken);
    }

    /// <summary>
    /// Runs one pass of <see cref="IFireDueRemindersUseCase"/>, resolved from a scope of its own —
    /// never from the scope an HTTP request is using — the same way <c>AppTemplate.Worker</c>'s
    /// background service does.
    /// </summary>
    protected async Task<int> FireDueRemindersAsync()
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var useCase = scope.ServiceProvider.GetRequiredService<IFireDueRemindersUseCase>();

        return (await useCase.ExecuteAsync(TestToken)).Value;
    }
}

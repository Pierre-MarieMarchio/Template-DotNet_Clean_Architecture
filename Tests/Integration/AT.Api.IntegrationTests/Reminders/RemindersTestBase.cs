using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using AppTemplate.Api.Features.Reminders.Contracts.Requests;
using AppTemplate.Api.Features.Reminders.Contracts.Responses;
using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Application.Features.Reminders.UseCases.Commands.FireDueReminders;
using AppTemplate.Infrastructure.InMemory.Features.Reminders;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AppTemplate.Api.IntegrationTests.Reminders;

public abstract class RemindersTestBase(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    protected const string RemindersRoute = "/api/v1/reminders";

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

    protected async Task BackdateReminderAsync(Guid reminderId, DateTimeOffset dueAt)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await context.Database.ExecuteSqlAsync(
            FormattableStringFactory.Create(
                $$"""UPDATE {{DatabaseServer.Table("reminders", "Reminders")}} SET "DueAt" = {0} WHERE "Id" = {1}""",
                DatabaseServer.Value(dueAt),
                DatabaseServer.Value(reminderId)),
            TestToken);
    }

    protected async Task PutInBackoffAsync(Guid reminderId, DateTimeOffset nextAttemptAt)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await context.Database.ExecuteSqlAsync(
            FormattableStringFactory.Create(
                $$"""UPDATE {{DatabaseServer.Table("reminders", "Reminders")}} SET "FailedAttempts" = 1, "NextAttemptAt" = {0} WHERE "Id" = {1}""",
                DatabaseServer.Value(nextAttemptAt),
                DatabaseServer.Value(reminderId)),
            TestToken);
    }

    protected async Task CompleteTodoItemWithoutRaisingAnEventAsync(Guid todoItemId, DateTimeOffset completedAt)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await context.Database.ExecuteSqlAsync(
            FormattableStringFactory.Create(
                $$"""UPDATE {{DatabaseServer.Table("todo", "TodoItems")}} SET "CompletedAt" = {0} WHERE "Id" = {1}""",
                DatabaseServer.Value(completedAt),
                DatabaseServer.Value(todoItemId)),
            TestToken);
    }

    protected async Task<int> FireDueRemindersAsync()
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var useCase = scope.ServiceProvider.GetRequiredService<IFireDueRemindersUseCase>();

        return (await useCase.ExecuteAsync(TestToken)).Value;
    }
}

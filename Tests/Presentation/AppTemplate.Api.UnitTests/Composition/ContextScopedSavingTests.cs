using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Idempotency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Reminders.Entities;
using AppTemplate.Domain.Features.Reminders.Repositories;
using AppTemplate.Domain.Features.Reminders.ValueObjects;
using AppTemplate.Domain.Features.TodoLists.Entities;
using AppTemplate.Domain.Features.TodoLists.Events;
using AppTemplate.Domain.Features.TodoLists.Repositories;
//#if (localAuth)
using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using AppTemplate.Infrastructure.Core.Common.Saving;
//#endif
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Composition;

/// <summary>
/// A save commits, stamps, flushes and publishes the work staged on the context instance being
/// saved, and nothing staged on another. The business context is created a second time per request
/// for idempotency claims, and the local accounts have a context of their own; each of those saves
/// is its own transaction.
/// </summary>
/// <remarks>
/// Real module composition, real contexts, real commits: see <see cref="SqliteSavingHost"/> for what
/// stands in for PostgreSQL. The same two isolation scenarios run against PostgreSQL in
/// <c>AppTemplate.Api.IntegrationTests</c>.
/// </remarks>
public sealed class ContextScopedSavingTests
{
    private static readonly Guid _caller = Guid.CreateVersion7();

    private static CancellationToken Token => TestContext.Current.CancellationToken;
    //#if (localAuth)

    /// <summary>
    /// An account write and a business write staged in one request are two commits. The account's
    /// must neither map the business aggregate onto rows its model does not have, nor take the event
    /// the business write raised.
    /// </summary>
    [Fact]
    public async Task SavingTheAuthContext_NeitherFlushesNorPublishesAPendingBusinessWrite()
    {
        await using var host = await SqliteSavingHost.StartAsync(Token);
        await using var scope = host.RequestScope(_caller);
        var services = scope.ServiceProvider;

        services.GetRequiredService<ITodoListRepository>().Add(ANewList("Groceries"));

        services.GetRequiredService<AuthDbContext>().Users.Add(AnAccount());
        await services.GetRequiredService<IContextUnitOfWork<AuthDbContext>>().SaveChangesAsync(Token);

        host.Published.All.ShouldBeEmpty(
            "the account commit says nothing about the list, which is not written yet");

        await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Token);

        var publication = host.Published.All.ShouldHaveSingleItem();
        publication.Event.ShouldBeOfType<TodoListCreatedDomainEvent>();
        publication.ListWasCommitted.ShouldBeTrue("published after the commit that wrote the list");
    }
    //#endif

    /// <summary>
    /// The idempotency store commits its claim through a second <see cref="AppDbContext"/> in the
    /// same request. That commit is not the business one, so the event a staged business write raised
    /// must wait for the business commit.
    /// </summary>
    [Fact]
    public async Task SavingAnIdempotencyClaim_DoesNotPublishAPendingBusinessEvent()
    {
        await using var host = await SqliteSavingHost.StartAsync(Token);
        await using var scope = host.RequestScope(_caller);
        var services = scope.ServiceProvider;

        services.GetRequiredService<ITodoListRepository>().Add(ANewList("Groceries"));

        var claim = await services.GetRequiredService<IIdempotencyStore>().ClaimAsync(
            AKey("create-groceries"),
            expiresAt: DateTimeOffset.UtcNow.AddDays(1),
            claimedUntil: DateTimeOffset.UtcNow.AddMinutes(1),
            Token);

        claim.Status.ShouldBe(IdempotencyStatus.Claimed);
        host.Published.All.ShouldBeEmpty(
            "the claim committed in a transaction of its own; the list it guards is not written yet");

        await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Token);

        var publication = host.Published.All.ShouldHaveSingleItem();
        publication.Event.ShouldBeOfType<TodoListCreatedDomainEvent>();
        publication.ListWasCommitted.ShouldBeTrue();
    }

    /// <summary>
    /// A consumer runs after the commit and may save again through the same scoped context, from
    /// inside that commit's callback. That nested save flushes, commits and publishes its own work.
    /// </summary>
    [Fact]
    public async Task AConsumerThatSavesAfterTheCommit_CommitsItsOwnWrite()
    {
        await using var host = await SqliteSavingHost.StartAsync(Token);
        var (listId, itemId) = await AListWithOneItemAsync(host);
        var reminderId = await AReminderForAsync(host, listId, itemId);

        await using (var scope = host.RequestScope(_caller))
        {
            var services = scope.ServiceProvider;
            var list = (await services.GetRequiredService<ITodoListRepository>().GetAsync(listId, Token))!;
            list.CompleteItem(itemId, DateTimeOffset.UtcNow);

            await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Token);
        }

        host.Published.OfType<TodoItemCompletedDomainEvent>().ShouldHaveSingleItem();

        await using var check = host.RequestScope(_caller);
        var reminder = await check.ServiceProvider
            .GetRequiredService<IReminderRepository>()
            .GetAsync(reminderId, Token);
        reminder!.State.ShouldBe(
            ReminderState.Cancelled,
            "the reminders consumer saved from inside the completion's commit callback");
    }

    /// <summary>
    /// A rejected save publishes nothing and keeps its events; the retry on the same context publishes
    /// them once, after its own commit.
    /// </summary>
    [Fact]
    public async Task AConflictingSave_PublishesNothing_AndItsRetryPublishesOnce()
    {
        await using var host = await SqliteSavingHost.StartAsync(Token);
        var (listId, itemId) = await AListWithOneItemAsync(host);

        await using var scope = host.RequestScope(_caller);
        var services = scope.ServiceProvider;
        var list = (await services.GetRequiredService<ITodoListRepository>().GetAsync(listId, Token))!;
        list.CompleteItem(itemId, DateTimeOffset.UtcNow);

        await host.ExecuteOnBusinessStoreAsync("UPDATE \"TodoLists\" SET \"xmin\" = \"xmin\" + 1", Token);

        await Should.ThrowAsync<ConcurrencyConflictException>(
            () => services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Token));
        host.Published.OfType<TodoItemCompletedDomainEvent>().ShouldBeEmpty();

        await host.ExecuteOnBusinessStoreAsync("UPDATE \"TodoLists\" SET \"xmin\" = \"xmin\" - 1", Token);
        await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Token);

        host.Published.OfType<TodoItemCompletedDomainEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task ASynchronousSave_FlushesAndPublishesToo()
    {
        await using var host = await SqliteSavingHost.StartAsync(Token);
        await using var scope = host.RequestScope(_caller);
        var services = scope.ServiceProvider;

        services.GetRequiredService<ITodoListRepository>().Add(ANewList("Groceries"));
        services.GetRequiredService<AppDbContext>().SaveChanges();

        host.Published.All.ShouldHaveSingleItem().ListWasCommitted.ShouldBeTrue();
    }

    /// <summary>
    /// The audit stamps name the caller of a request and nobody for a background pass, and the
    /// aggregate a use case still holds carries the token the store just assigned.
    /// </summary>
    [Fact]
    public async Task ABusinessSave_StampsTheScopesActor_AndReadsTheStoresVersionBack()
    {
        await using var host = await SqliteSavingHost.StartAsync(Token);
        var fromRequest = ANewList("From a request");
        var fromBackground = ANewList("From a background pass");

        await using (var request = host.RequestScope(_caller))
        {
            request.ServiceProvider.GetRequiredService<ITodoListRepository>().Add(fromRequest);
            await request.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Token);
        }

        await using (var background = host.BackgroundScope())
        {
            background.ServiceProvider.GetRequiredService<ITodoListRepository>().Add(fromBackground);
            await background.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Token);
        }

        fromRequest.Version.ShouldBe(1u, "the token the store assigned on insert");

        await using var check = host.RequestScope(_caller);
        var repository = check.ServiceProvider.GetRequiredService<ITodoListRepository>();
        (await repository.GetAsync(fromRequest.Id, Token))!.CreatedBy.ShouldBe(_caller);
        (await repository.GetAsync(fromBackground.Id, Token))!.CreatedBy.ShouldBeNull();
    }

    private static TodoList ANewList(string name) =>
        TodoList.Create(UserId.Create(_caller), name, DateTimeOffset.UtcNow);
    //#if (localAuth)

    private static AppUser AnAccount()
    {
        string name = $"someone-{Guid.NewGuid():N}";

        return new AppUser
        {
            Id = Guid.CreateVersion7(),
            UserName = name,
            NormalizedUserName = name.ToUpperInvariant(),
            Email = $"{name}@example.invalid",
            NormalizedEmail = $"{name}@example.invalid".ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString(),
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
    //#endif

    private static IdempotencyKey AKey(string key) =>
        IdempotencyKey.Create(_caller, key, "POST /api/v1/todo-lists", "fingerprint", maxKeyLength: 64).Value;

    private static async Task<(Guid ListId, Guid ItemId)> AListWithOneItemAsync(SqliteSavingHost host)
    {
        await using var scope = host.RequestScope(_caller);
        var list = ANewList("Chores");
        var itemId = list.AddItem("Do the thing", description: null);

        scope.ServiceProvider.GetRequiredService<ITodoListRepository>().Add(list);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Token);

        return (list.Id, itemId);
    }

    private static async Task<Guid> AReminderForAsync(SqliteSavingHost host, Guid listId, Guid itemId)
    {
        await using var scope = host.RequestScope(_caller);
        var reminder = Reminder.Schedule(
            UserId.Create(_caller),
            listId,
            itemId,
            dueAt: DateTimeOffset.UtcNow.AddDays(1),
            now: DateTimeOffset.UtcNow);

        scope.ServiceProvider.GetRequiredService<IReminderRepository>().Add(reminder);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Token);

        return reminder.Id;
    }
}

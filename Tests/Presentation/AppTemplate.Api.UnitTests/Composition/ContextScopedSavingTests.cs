using AppTemplate.Application.Core.Common.Concurrency;
using AppTemplate.Application.Core.Common.Idempotency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Reminders.Model.Entities;
using AppTemplate.Domain.Features.Reminders.Model.ValueObjects;
using AppTemplate.Domain.Features.Reminders.Repositories;
using AppTemplate.Domain.Features.TodoLists.Model.Entities;
using AppTemplate.Domain.Features.TodoLists.Model.Events;
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

public sealed class ContextScopedSavingTests
{
    private static readonly Guid _caller = Guid.CreateVersion7();

    private static CancellationToken Token => TestContext.Current.CancellationToken;
    //#if (localAuth)

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

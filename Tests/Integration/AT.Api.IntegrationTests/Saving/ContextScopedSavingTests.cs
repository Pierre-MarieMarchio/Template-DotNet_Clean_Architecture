using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Application.Core.Common.Idempotency;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.TodoLists.Model.Entities;
using AppTemplate.Domain.Features.TodoLists.Repositories;
//#if (localAuth)
using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Auth.Features.Accounts.Rows.Models;
using AppTemplate.Infrastructure.Core.Common.Saving;
//#endif
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Saving;

public sealed class ContextScopedSavingTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    //#if (localAuth)
    [Fact]
    public async Task SavingTheAuthContext_NeitherFlushesNorPublishesAPendingBusinessWrite()
    {
        var (listId, itemId) = await AListWithOneItemAsync();

        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        await CompleteWithoutSavingAsync(services, listId, itemId);

        services.GetRequiredService<AuthDbContext>().Users.Add(AnAccount());
        await services.GetRequiredService<IContextUnitOfWork<AuthDbContext>>().SaveChangesAsync(TestToken);

        Fixture.DomainEvents.CompletedItems.ShouldBeEmpty("the account commit did not complete the item");

        await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestToken);

        Fixture.DomainEvents.CompletedItems.ShouldHaveSingleItem().TodoItemId.ShouldBe(itemId);
    }

    //#endif
    [Fact]
    public async Task SavingAnIdempotencyClaim_DoesNotPublishAPendingBusinessEvent()
    {
        var (listId, itemId) = await AListWithOneItemAsync();

        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        await CompleteWithoutSavingAsync(services, listId, itemId);

        var now = Clock.UtcNow;
        var claim = await services.GetRequiredService<IIdempotencyStore>()
            .ClaimAsync(AKey("complete-the-item"), now.AddHours(24), now.AddMinutes(15), TestToken);

        claim.Status.ShouldBe(IdempotencyStatus.Claimed);
        Fixture.DomainEvents.CompletedItems.ShouldBeEmpty(
            "the claim committed in a transaction of its own; the completion is not written yet");

        await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestToken);

        Fixture.DomainEvents.CompletedItems.ShouldHaveSingleItem().TodoItemId.ShouldBe(itemId);
    }

    private async Task<(Guid ListId, Guid ItemId)> AListWithOneItemAsync()
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var list = TodoList.Create(UserId.Create(Guid.CreateVersion7()), "Chores", Clock.UtcNow);
        var itemId = list.AddItem("Do the thing", description: null);

        scope.ServiceProvider.GetRequiredService<ITodoListRepository>().Add(list);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestToken);

        Fixture.DomainEvents.Clear();

        return (list.Id, itemId);
    }

    private async Task CompleteWithoutSavingAsync(IServiceProvider services, Guid listId, Guid itemId)
    {
        var list = await services.GetRequiredService<ITodoListRepository>().GetAsync(listId, TestToken);
        list.ShouldNotBeNull().CompleteItem(itemId, Clock.UtcNow);
    }

    //#if (localAuth)
    private AppUser AnAccount()
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
            CreatedAt = Clock.UtcNow,
        };
    }

    //#endif
    private static IdempotencyKey AKey(string key) =>
        IdempotencyKey.Create(
            userId: Guid.NewGuid(),
            key: key,
            endpoint: "POST /api/v1/todo-lists/{id}/items/{itemId}/complete",
            fingerprint: new string('a', 64),
            maxKeyLength: 512).Value;
}

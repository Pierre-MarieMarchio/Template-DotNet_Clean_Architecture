using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.TodoLists.Model.Entities;
using AppTemplate.Domain.Features.TodoLists.Repositories;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Composition;

public sealed class TodoListOwnerQueryTests
{
    private static readonly UserId _owner = UserId.Create(Guid.CreateVersion7());
    private static readonly UserId _stranger = UserId.Create(Guid.CreateVersion7());

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheOwnersRead_LoadsTheList()
    {
        await using var host = await SqliteSavingHost.StartAsync(Token);
        var listId = await AListOfTheOwnerAsync(host);

        await using var scope = host.RequestScope(_owner.Value);
        var services = scope.ServiceProvider;

        var loaded = await services.GetRequiredService<ITodoListRepository>().GetAsync(_owner, listId, Token);

        loaded.ShouldNotBeNull().Id.ShouldBe(listId);
        services.GetRequiredService<AppDbContext>().ChangeTracker.Entries().ShouldNotBeEmpty(
            "a loaded list is tracked, which is what lets the stranger's read below show that no row came back");
    }

    [Fact]
    public async Task AStrangersRead_FindsNothing_AndBringsNoRowIntoTheContext()
    {
        await using var host = await SqliteSavingHost.StartAsync(Token);
        var listId = await AListOfTheOwnerAsync(host);

        await using var scope = host.RequestScope(_stranger.Value);
        var services = scope.ServiceProvider;

        var loaded = await services.GetRequiredService<ITodoListRepository>().GetAsync(_stranger, listId, Token);

        loaded.ShouldBeNull();
        services.GetRequiredService<AppDbContext>().ChangeTracker.Entries().ShouldBeEmpty(
            "the owner is in the query, so the database never returned the owner's row to the stranger");
    }

    [Fact]
    public async Task AListAlreadyLoadedInTheScope_IsNotHandedToAnotherOwner()
    {
        await using var host = await SqliteSavingHost.StartAsync(Token);
        var listId = await AListOfTheOwnerAsync(host);

        await using var scope = host.RequestScope(_owner.Value);
        var repository = scope.ServiceProvider.GetRequiredService<ITodoListRepository>();

        (await repository.GetAsync(_owner, listId, Token)).ShouldNotBeNull();

        (await repository.GetAsync(_stranger, listId, Token)).ShouldBeNull(
            "the identity map answers with the owner's predicate too");
    }

    private static async Task<Guid> AListOfTheOwnerAsync(SqliteSavingHost host)
    {
        await using var scope = host.RequestScope(_owner.Value);
        var list = TodoList.Create(_owner, "Groceries", DateTimeOffset.UtcNow);

        scope.ServiceProvider.GetRequiredService<ITodoListRepository>().Add(list);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Token);

        return list.Id;
    }
}

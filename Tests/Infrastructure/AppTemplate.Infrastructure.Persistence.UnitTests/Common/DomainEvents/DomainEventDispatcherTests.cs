using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Domain.Common.Events;
using AppTemplate.Domain.Features.TodoLists.Events;
using AppTemplate.Infrastructure.Persistence.Common.DomainEvents;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Common.DomainEvents;

/// <summary>
/// Consumers are found by the event's <em>runtime</em> type, not by the interface it is held as, and every
/// consumer registered for that type runs.
/// </summary>
public sealed class DomainEventDispatcherTests
{
    private static readonly TodoListCreatedDomainEvent _created = new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        "Groceries",
        new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero));

    [Fact]
    public async Task DispatchAsync_ReachesEveryConsumerRegisteredForTheEvent()
    {
        var first = new CountingConsumer();
        var second = new CountingConsumer();

        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventConsumer<TodoListCreatedDomainEvent>>(first);
        services.AddSingleton<IDomainEventConsumer<TodoListCreatedDomainEvent>>(second);

        await using var provider = services.BuildServiceProvider();

        // Held as the interface, so a dispatcher that keyed on the compile-time type would find nobody.
        IDomainEvent raised = _created;
        await new DomainEventDispatcher(provider).DispatchAsync(raised, TestContext.Current.CancellationToken);

        first.Consumed.ShouldBe(1);
        second.Consumed.ShouldBe(1);
    }

    [Fact]
    public async Task DispatchAsync_DoesNothing_WhenNobodyConsumesTheEvent()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();

        await new DomainEventDispatcher(provider).DispatchAsync(
            _created,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DispatchAsync_RejectsNull()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();

        await Should.ThrowAsync<ArgumentNullException>(
            async () => await new DomainEventDispatcher(provider).DispatchAsync(null!));
    }

    private sealed class CountingConsumer : IDomainEventConsumer<TodoListCreatedDomainEvent>
    {
        internal int Consumed { get; private set; }

        public Task ConsumeAsync(
            TodoListCreatedDomainEvent domainEvent,
            CancellationToken cancellationToken = default)
        {
            Consumed++;

            return Task.CompletedTask;
        }
    }
}

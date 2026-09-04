using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Domain.Common.Events;
using AppTemplate.Domain.Features.TodoLists.Events;
using AppTemplate.Infrastructure.Persistence.Common.Saving.DomainEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Common.Saving.DomainEvents;

/// <summary>
/// Consumers are found by the event's <em>runtime</em> type, not by the interface it is held as, and every
/// consumer registered for that type runs — even one after a sibling has thrown.
/// </summary>
public sealed class DomainEventDispatcherTests
{
    private static readonly TodoListCreatedDomainEvent _created = new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        "Groceries",
        new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero));

    private readonly RecordingLogger<DomainEventDispatcher> _logger = new();

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
        await Dispatcher(provider).DispatchAsync(raised, TestContext.Current.CancellationToken);

        first.Consumed.ShouldBe(1);
        second.Consumed.ShouldBe(1);
    }

    [Fact]
    public async Task DispatchAsync_DoesNothing_WhenNobodyConsumesTheEvent()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();

        await Dispatcher(provider).DispatchAsync(
            _created,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DispatchAsync_RejectsNull()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();

        await Should.ThrowAsync<ArgumentNullException>(
            async () => await Dispatcher(provider).DispatchAsync(null!));
    }

    /// <summary>The whole point of the isolation: the first consumer's failure must not stop the second.</summary>
    [Fact]
    public async Task DispatchAsync_StillRunsTheSecondConsumer_WhenTheFirstThrows()
    {
        var first = new ThrowingConsumer(new InvalidOperationException("the first consumer is broken"));
        var second = new CountingConsumer();

        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventConsumer<TodoListCreatedDomainEvent>>(first);
        services.AddSingleton<IDomainEventConsumer<TodoListCreatedDomainEvent>>(second);

        await using var provider = services.BuildServiceProvider();

        await Dispatcher(provider).DispatchAsync(_created, TestContext.Current.CancellationToken);

        second.Consumed.ShouldBe(1, "a sibling consumer's failure must not stop this one from running");
    }

    [Fact]
    public async Task DispatchAsync_LogsAThrowingConsumer_WithTheEventAndConsumerType()
    {
        var failure = new InvalidOperationException("the consumer could not reach the mail relay");
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventConsumer<TodoListCreatedDomainEvent>>(new ThrowingConsumer(failure));

        await using var provider = services.BuildServiceProvider();

        await Dispatcher(provider).DispatchAsync(_created, TestContext.Current.CancellationToken);

        var entry = _logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Error);
        entry.Exception.ShouldBeSameAs(failure);
        entry.Message.ShouldContain(nameof(TodoListCreatedDomainEvent));
        entry.Message.ShouldContain(nameof(ThrowingConsumer));
    }

    /// <summary>
    /// A cancelled request is not a failed consumer: the token's own cancellation must propagate rather
    /// than be logged and swallowed like any other exception.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_PropagatesCancellation_RatherThanSwallowingIt()
    {
        using var cancellation = new CancellationTokenSource();
        var consumer = new CancelingConsumer(cancellation);

        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventConsumer<TodoListCreatedDomainEvent>>(consumer);

        await using var provider = services.BuildServiceProvider();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await Dispatcher(provider).DispatchAsync(_created, cancellation.Token));

        _logger.Entries.ShouldBeEmpty("a cancelled request is not a consumer failure");
    }

    /// <summary>
    /// A registration that is not an <see cref="IDomainEventConsumer"/> is a composition bug, not a
    /// consumer failure, and the per-consumer isolation must not turn it into a silent no-op.
    /// </summary>
    [Fact]
    public async Task DispatchAsync_StillThrows_WhenARegistrationIsNotAConsumer()
    {
        // The normal DI surface refuses this at registration: AddSingleton(Type, object) already
        // checks the instance is assignable to the service type, and IDomainEventConsumer<T> always
        // implies IDomainEventConsumer, so no ordinary container ever hands the loop below something
        // that fails its cast. A hand-written provider is the only way to force that shape and prove
        // the isolation added around it still lets this exception through.
        var provider = new MisconfiguredServiceProvider();

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await Dispatcher(provider).DispatchAsync(_created, TestContext.Current.CancellationToken));
    }

    private DomainEventDispatcher Dispatcher(IServiceProvider provider) => new(provider, _logger);

    /// <summary>Answers every <c>IEnumerable&lt;&gt;</c> request with a single object that consumes nothing.</summary>
    private sealed class MisconfiguredServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                ? new object[] { new object() }
                : null;
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

    private sealed class ThrowingConsumer(Exception failure) : IDomainEventConsumer<TodoListCreatedDomainEvent>
    {
        public Task ConsumeAsync(
            TodoListCreatedDomainEvent domainEvent,
            CancellationToken cancellationToken = default) => Task.FromException(failure);
    }

    private sealed class CancelingConsumer(CancellationTokenSource cancellation)
        : IDomainEventConsumer<TodoListCreatedDomainEvent>
    {
        public Task ConsumeAsync(TodoListCreatedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();

            return Task.CompletedTask;
        }
    }
}

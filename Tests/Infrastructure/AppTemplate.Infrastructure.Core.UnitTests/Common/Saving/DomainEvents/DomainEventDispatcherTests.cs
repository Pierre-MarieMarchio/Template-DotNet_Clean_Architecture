using AppTemplate.Application.Core.Common.Events;
using AppTemplate.Domain.Core.Common.Events;
using AppTemplate.Infrastructure.Core.Common.Saving.DomainEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Core.UnitTests.Common.Saving.DomainEvents;

public sealed class DomainEventDispatcherTests
{
    private static readonly SomethingHappenedDomainEvent _created =
        new(Guid.CreateVersion7(), new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero));

    private readonly RecordingLogger<DomainEventDispatcher> _logger = new();

    [Fact]
    public async Task DispatchAsync_ReachesEveryConsumerRegisteredForTheEvent()
    {
        var first = new CountingConsumer();
        var second = new CountingConsumer();

        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventConsumer<SomethingHappenedDomainEvent>>(first);
        services.AddSingleton<IDomainEventConsumer<SomethingHappenedDomainEvent>>(second);

        await using var provider = services.BuildServiceProvider();

        IDomainEvent heldAsTheInterface = _created;
        await Dispatcher(provider).DispatchAsync(heldAsTheInterface, TestContext.Current.CancellationToken);

        first.Consumed.ShouldBe(1);
        second.Consumed.ShouldBe(1);
    }

    [Fact]
    public async Task DispatchAsync_DoesNothing_WhenNobodyConsumesTheEvent()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();

        await Should.NotThrowAsync(
            () => Dispatcher(provider).DispatchAsync(
                _created,
                TestContext.Current.CancellationToken));

        _logger.Entries.ShouldBeEmpty("an event nobody consumes is not a consumer failure");
    }

    [Fact]
    public async Task DispatchAsync_RejectsNull()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();

        await Should.ThrowAsync<ArgumentNullException>(
            async () => await Dispatcher(provider).DispatchAsync(null!));
    }

    [Fact]
    public async Task DispatchAsync_StillRunsTheSecondConsumer_WhenTheFirstThrows()
    {
        var first = new ThrowingConsumer(new InvalidOperationException("the first consumer is broken"));
        var second = new CountingConsumer();

        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventConsumer<SomethingHappenedDomainEvent>>(first);
        services.AddSingleton<IDomainEventConsumer<SomethingHappenedDomainEvent>>(second);

        await using var provider = services.BuildServiceProvider();

        await Dispatcher(provider).DispatchAsync(_created, TestContext.Current.CancellationToken);

        second.Consumed.ShouldBe(1, "a sibling consumer's failure must not stop this one from running");
    }

    [Fact]
    public async Task DispatchAsync_LogsAThrowingConsumer_WithTheEventAndConsumerType()
    {
        var failure = new InvalidOperationException("the consumer could not reach the mail relay");
        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventConsumer<SomethingHappenedDomainEvent>>(new ThrowingConsumer(failure));

        await using var provider = services.BuildServiceProvider();

        await Dispatcher(provider).DispatchAsync(_created, TestContext.Current.CancellationToken);

        var entry = _logger.Entries.ShouldHaveSingleItem();
        entry.Level.ShouldBe(LogLevel.Error);
        entry.Exception.ShouldBeSameAs(failure);
        entry.Message.ShouldContain(nameof(SomethingHappenedDomainEvent));
        entry.Message.ShouldContain(nameof(ThrowingConsumer));
    }

    [Fact]
    public async Task DispatchAsync_PropagatesCancellation_RatherThanSwallowingIt()
    {
        using var cancellation = new CancellationTokenSource();
        var consumer = new CancelingConsumer(cancellation);

        var services = new ServiceCollection();
        services.AddSingleton<IDomainEventConsumer<SomethingHappenedDomainEvent>>(consumer);

        await using var provider = services.BuildServiceProvider();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await Dispatcher(provider).DispatchAsync(_created, cancellation.Token));

        _logger.Entries.ShouldBeEmpty("a cancelled request is not a consumer failure");
    }

    [Fact]
    public async Task DispatchAsync_StillThrows_WhenARegistrationIsNotAConsumer()
    {
        var provider = new MisconfiguredServiceProvider();

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await Dispatcher(provider).DispatchAsync(_created, TestContext.Current.CancellationToken));
    }

    private DomainEventDispatcher Dispatcher(IServiceProvider provider) => new(provider, _logger);

    private sealed record SomethingHappenedDomainEvent(Guid ThingId, DateTimeOffset OccurredOn) : IDomainEvent;

    private sealed class MisconfiguredServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>)
                ? new object[] { new object() }
                : null;
    }

    private sealed class CountingConsumer : IDomainEventConsumer<SomethingHappenedDomainEvent>
    {
        internal int Consumed { get; private set; }

        public Task ConsumeAsync(
            SomethingHappenedDomainEvent domainEvent,
            CancellationToken cancellationToken = default)
        {
            Consumed++;

            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingConsumer(Exception failure) : IDomainEventConsumer<SomethingHappenedDomainEvent>
    {
        public Task ConsumeAsync(
            SomethingHappenedDomainEvent domainEvent,
            CancellationToken cancellationToken = default) => Task.FromException(failure);
    }

    private sealed class CancelingConsumer(CancellationTokenSource cancellation)
        : IDomainEventConsumer<SomethingHappenedDomainEvent>
    {
        public Task ConsumeAsync(SomethingHappenedDomainEvent domainEvent, CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();

            return Task.CompletedTask;
        }
    }
}

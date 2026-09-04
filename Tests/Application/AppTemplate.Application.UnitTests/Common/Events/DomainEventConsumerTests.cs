using AppTemplate.Application.Common.Events;
using AppTemplate.Domain.Common.Events;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Common.Events;

/// <summary>
/// Covers the bridge from the non-generic <see cref="IDomainEventConsumer"/> the dispatcher calls to
/// the typed <see cref="IDomainEventConsumer{TEvent}"/> a consumer implements. The dispatcher hands
/// over an <see cref="IDomainEvent"/>, so a mis-registration is only detectable here.
/// </summary>
public sealed class DomainEventConsumerTests
{
    private sealed record AnEvent(DateTimeOffset OccurredOn) : IDomainEvent;

    private sealed record AnotherEvent(DateTimeOffset OccurredOn) : IDomainEvent;

    private sealed class RecordingConsumer : IDomainEventConsumer<AnEvent>
    {
        public AnEvent? Received { get; private set; }

        public Task ConsumeAsync(AnEvent domainEvent, CancellationToken cancellationToken = default)
        {
            Received = domainEvent;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task TheNonGenericEntryPoint_ForwardsAMatchingEventToTheTypedMethod()
    {
        var consumer = new RecordingConsumer();
        var domainEvent = new AnEvent(DateTimeOffset.UnixEpoch);

        await ((IDomainEventConsumer)consumer).ConsumeAsync(
            domainEvent,
            TestContext.Current.CancellationToken);

        consumer.Received.ShouldBe(domainEvent);
    }

    /// <summary>
    /// A hard cast here throws an <see cref="InvalidCastException"/> naming neither the consumer nor
    /// the event, which are the only two facts that locate the faulty registration.
    /// </summary>
    [Fact]
    public async Task TheNonGenericEntryPoint_RefusesAnEventOfTheWrongType_NamingBoth()
    {
        var consumer = new RecordingConsumer();

        var exception = await Should.ThrowAsync<ArgumentException>(() =>
            ((IDomainEventConsumer)consumer).ConsumeAsync(
                new AnotherEvent(DateTimeOffset.UnixEpoch),
                TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(nameof(RecordingConsumer));
        exception.Message.ShouldContain(nameof(AnEvent));
        exception.Message.ShouldContain(nameof(AnotherEvent));
        consumer.Received.ShouldBeNull();
    }

    [Fact]
    public async Task TheNonGenericEntryPoint_RefusesANullEvent()
    {
        var consumer = new RecordingConsumer();

        await Should.ThrowAsync<ArgumentException>(() =>
            ((IDomainEventConsumer)consumer).ConsumeAsync(
                null!,
                TestContext.Current.CancellationToken));
    }
}

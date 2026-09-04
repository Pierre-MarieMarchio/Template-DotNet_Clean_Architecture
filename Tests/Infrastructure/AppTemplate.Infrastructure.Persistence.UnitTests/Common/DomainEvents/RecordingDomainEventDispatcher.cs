using AppTemplate.Domain.Common.Events;
using AppTemplate.Infrastructure.Persistence.Common.DomainEvents;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Common.DomainEvents;

/// <summary>
/// Records what it was asked to publish, and throws for the events a test tells it to.
/// </summary>
/// <remarks>
/// Hand-written rather than substituted, because <c>IDomainEventDispatcher</c> is internal to the
/// persistence assembly: a dynamic proxy would need that assembly to trust the proxy generator, which is
/// a wider grant than one test double is worth.
/// </remarks>
internal sealed class RecordingDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly Func<IDomainEvent, Exception?> _failure;

    internal RecordingDomainEventDispatcher()
        : this(_ => null)
    {
    }

    internal RecordingDomainEventDispatcher(Func<IDomainEvent, Exception?> failure) => _failure = failure;

    internal List<IDomainEvent> Dispatched { get; } = [];

    public Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        if (_failure(domainEvent) is { } consumerFailure)
        {
            return Task.FromException(consumerFailure);
        }

        Dispatched.Add(domainEvent);

        return Task.CompletedTask;
    }
}

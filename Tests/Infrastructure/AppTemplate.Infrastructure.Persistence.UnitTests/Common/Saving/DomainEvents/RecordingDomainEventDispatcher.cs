using AppTemplate.Domain.Core.Common.Events;
using AppTemplate.Infrastructure.Core.Common.Saving.DomainEvents;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Common.Saving.DomainEvents;

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

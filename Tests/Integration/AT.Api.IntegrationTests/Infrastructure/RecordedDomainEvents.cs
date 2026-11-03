using AppTemplate.Domain.Core.Common.Events;
using AppTemplate.Infrastructure.Core.Common.Saving.DomainEvents;
using Microsoft.Extensions.DependencyInjection;

namespace AppTemplate.Api.IntegrationTests.Infrastructure;

public sealed class RecordedDomainEvents
{
    private readonly object _gate = new();
    private readonly List<IDomainEvent> _events = [];

    public IReadOnlyList<IDomainEvent> All
    {
        get
        {
            lock (_gate)
            {
                return [.. _events];
            }
        }
    }

    public IReadOnlyList<TEvent> Of<TEvent>()
        where TEvent : IDomainEvent => [.. All.OfType<TEvent>()];

    public void Record(IDomainEvent domainEvent)
    {
        lock (_gate)
        {
            _events.Add(domainEvent);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _events.Clear();
        }
    }
}

internal sealed class RecordingDomainEventDispatcher(
    IDomainEventDispatcher inner,
    RecordedDomainEvents recorded) : IDomainEventDispatcher
{
    public Task DispatchAsync(IDomainEvent domainEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        recorded.Record(domainEvent);

        return inner.DispatchAsync(domainEvent, cancellationToken);
    }
}

internal static class RecordedDomainEventsExtensions
{
    internal static IServiceCollection AddRecordedDomainEvents(this IServiceCollection services)
    {
        services.AddSingleton<RecordedDomainEvents>();
        services.AddScoped<IDomainEventDispatcher>(serviceProvider => new RecordingDomainEventDispatcher(
            ActivatorUtilities.CreateInstance<DomainEventDispatcher>(serviceProvider),
            serviceProvider.GetRequiredService<RecordedDomainEvents>()));

        return services;
    }
}

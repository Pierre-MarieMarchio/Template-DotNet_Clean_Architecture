using AppTemplate.Domain.Common.Events;
using AppTemplate.Domain.Common.Primitives;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.UnitTests.Common.Primitives;

public sealed class AggregateRootTests
{
    [Fact]
    public void DomainEvents_IsEmpty_ForANewAggregate() =>
        new SampleAggregate(Guid.CreateVersion7()).DomainEvents.ShouldBeEmpty();

    [Fact]
    public void RaiseDomainEvent_AppendsTheEvent()
    {
        var aggregate = new SampleAggregate(Guid.CreateVersion7());
        var domainEvent = new SampleDomainEvent(DateTimeOffset.UnixEpoch);

        aggregate.Raise(domainEvent);

        aggregate.DomainEvents.ShouldHaveSingleItem().ShouldBeSameAs(domainEvent);
    }

    /// <summary>Order matters: handlers may depend on the sequence the events happened in.</summary>
    [Fact]
    public void RaiseDomainEvent_PreservesTheOrderEventsWereRaisedIn()
    {
        var aggregate = new SampleAggregate(Guid.CreateVersion7());
        var first = new SampleDomainEvent(DateTimeOffset.UnixEpoch);
        var second = new SampleDomainEvent(DateTimeOffset.UnixEpoch.AddMinutes(1));

        aggregate.Raise(first);
        aggregate.Raise(second);

        aggregate.DomainEvents.ShouldBe([first, second]);
    }

    [Fact]
    public void RaiseDomainEvent_Rejects_ANullEvent()
    {
        var aggregate = new SampleAggregate(Guid.CreateVersion7());

        Should.Throw<ArgumentNullException>(() => aggregate.Raise(null!));
        aggregate.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void ClearDomainEvents_EmptiesTheCollection()
    {
        var aggregate = new SampleAggregate(Guid.CreateVersion7());
        aggregate.Raise(new SampleDomainEvent(DateTimeOffset.UnixEpoch));
        aggregate.Raise(new SampleDomainEvent(DateTimeOffset.UnixEpoch));

        aggregate.ClearDomainEvents();

        aggregate.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void ClearDomainEvents_IsSafeOnAnAggregateThatRaisedNothing()
    {
        var aggregate = new SampleAggregate(Guid.CreateVersion7());

        aggregate.ClearDomainEvents();

        aggregate.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void RaiseDomainEvent_WorksAgainAfterAClear()
    {
        var aggregate = new SampleAggregate(Guid.CreateVersion7());
        aggregate.Raise(new SampleDomainEvent(DateTimeOffset.UnixEpoch));
        aggregate.ClearDomainEvents();

        var afterClear = new SampleDomainEvent(DateTimeOffset.UnixEpoch.AddMinutes(1));
        aggregate.Raise(afterClear);

        aggregate.DomainEvents.ShouldHaveSingleItem().ShouldBeSameAs(afterClear);
    }

    /// <summary>
    /// The dispatcher receives a snapshot it cannot append to: an event added from
    /// outside the aggregate would be one nothing in the model ever decided to raise.
    /// </summary>
    [Fact]
    public void DomainEvents_CannotBeMutatedThroughTheExposedCollection()
    {
        var aggregate = new SampleAggregate(Guid.CreateVersion7());

        var asCollection = (ICollection<IDomainEvent>)aggregate.DomainEvents;

        asCollection.IsReadOnly.ShouldBeTrue();
        Should.Throw<NotSupportedException>(() => asCollection.Add(new SampleDomainEvent(DateTimeOffset.UnixEpoch)));
        aggregate.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void AnAggregateRoot_IsAlsoAnEntity()
    {
        var id = Guid.CreateVersion7();

        new SampleAggregate(id).ShouldBe(new SampleAggregate(id));
    }

    /// <summary>
    /// Infrastructure collects events through the non-generic marker, so a root must be
    /// reachable as <see cref="IAggregateRoot"/> without knowing its id type.
    /// </summary>
    [Fact]
    public void AnAggregateRoot_IsReachableThroughTheNonGenericMarker()
    {
        var aggregate = new SampleAggregate(Guid.CreateVersion7());
        aggregate.Raise(new SampleDomainEvent(DateTimeOffset.UnixEpoch));

        var asMarker = aggregate.ShouldBeAssignableTo<IAggregateRoot>();

        asMarker.DomainEvents.Count.ShouldBe(1);

        asMarker.ClearDomainEvents();

        asMarker.DomainEvents.ShouldBeEmpty();
    }
}

/// <summary>Exposes <c>RaiseDomainEvent</c> so the protected hook can be exercised.</summary>
internal sealed class SampleAggregate(Guid id) : AggregateRoot<Guid>(id)
{
    public void Raise(IDomainEvent domainEvent) => RaiseDomainEvent(domainEvent);
}

internal sealed record SampleDomainEvent(DateTimeOffset OccurredOn) : IDomainEvent;

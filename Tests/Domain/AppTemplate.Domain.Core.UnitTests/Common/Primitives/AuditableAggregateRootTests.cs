using AppTemplate.Domain.Core.Common.Abstractions;
using AppTemplate.Domain.Core.Common.Events;
using AppTemplate.Domain.Core.Common.Primitives;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.Core.UnitTests.Common.Primitives;

public sealed class AuditableAggregateRootTests
{
    private static readonly DateTimeOffset _at = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public void ANewAggregate_CarriesNoStamps()
    {
        var aggregate = new SampleAuditableAggregate(Guid.CreateVersion7());

        aggregate.Version.ShouldBe(0u);
        aggregate.CreatedAt.ShouldBe(default);
        aggregate.CreatedBy.ShouldBeNull();
        aggregate.LastModifiedAt.ShouldBeNull();
        aggregate.LastModifiedBy.ShouldBeNull();
    }

    [Fact]
    public void SetCreated_WritesBothCreationValues_AndLeavesTheModificationOnesAlone()
    {
        var aggregate = new SampleAuditableAggregate(Guid.CreateVersion7());
        var actor = Guid.CreateVersion7();

        ((IAuditable)aggregate).SetCreated(_at, actor);

        aggregate.CreatedAt.ShouldBe(_at);
        aggregate.CreatedBy.ShouldBe(actor);
        aggregate.LastModifiedAt.ShouldBeNull();
        aggregate.LastModifiedBy.ShouldBeNull();
    }

    [Fact]
    public void SetLastModified_WritesBothModificationValues()
    {
        var aggregate = new SampleAuditableAggregate(Guid.CreateVersion7());
        var actor = Guid.CreateVersion7();

        ((IAuditable)aggregate).SetLastModified(_at, actor);

        aggregate.LastModifiedAt.ShouldBe(_at);
        aggregate.LastModifiedBy.ShouldBe(actor);
    }

    /// <summary>An unattributable caller is a null actor rather than a missing stamp.</summary>
    [Fact]
    public void AnAnonymousWrite_StampsTheInstantAndNoActor()
    {
        var aggregate = new SampleAuditableAggregate(Guid.CreateVersion7());

        ((IAuditable)aggregate).SetCreated(_at, null);
        ((IAuditable)aggregate).SetLastModified(_at, null);

        aggregate.CreatedAt.ShouldBe(_at);
        aggregate.CreatedBy.ShouldBeNull();
        aggregate.LastModifiedAt.ShouldBe(_at);
        aggregate.LastModifiedBy.ShouldBeNull();
    }

    [Fact]
    public void SetVersion_IsReadableAsAProperty()
    {
        var aggregate = new SampleAuditableAggregate(Guid.CreateVersion7());

        ((IVersioned)aggregate).SetVersion(4_242u);

        aggregate.Version.ShouldBe(4_242u);
        ((IVersioned)aggregate).Version.ShouldBe(4_242u);
    }

    /// <summary>
    /// The whole reason both interfaces are implemented explicitly: code holding an aggregate
    /// cannot write a stamp without first admitting, in writing, that it is doing something a store
    /// does. Read off the derived type, because that is what application code holds.
    /// </summary>
    [Theory]
    [InlineData(nameof(IVersioned.Version))]
    [InlineData(nameof(IAuditable.CreatedAt))]
    [InlineData(nameof(IAuditable.CreatedBy))]
    [InlineData(nameof(IAuditable.LastModifiedAt))]
    [InlineData(nameof(IAuditable.LastModifiedBy))]
    public void AStamp_HasNoPublicSetter(string property) =>
        typeof(SampleAuditableAggregate).GetProperty(property)!
            .GetSetMethod(nonPublic: false)
            .ShouldBeNull($"'{property}' is written by the store through its interface, not by assignment.");

    [Theory]
    [InlineData(nameof(IVersioned.SetVersion))]
    [InlineData(nameof(IAuditable.SetCreated))]
    [InlineData(nameof(IAuditable.SetLastModified))]
    public void AStampWriter_IsNotOnThePublicSurface(string method) =>
        typeof(SampleAuditableAggregate).GetMethod(method)
            .ShouldBeNull($"'{method}' must be reachable only through the interface that declares it.");

    /// <summary>It is still an aggregate root, so the event mechanism has to survive the base.</summary>
    [Fact]
    public void AnAuditableRoot_IsStillAnAggregateRoot()
    {
        var aggregate = new SampleAuditableAggregate(Guid.CreateVersion7());
        var domainEvent = new SampleAuditableDomainEvent(_at);

        aggregate.Raise(domainEvent);

        aggregate.ShouldBeAssignableTo<IAggregateRoot>();
        aggregate.DomainEvents.ShouldHaveSingleItem().ShouldBeSameAs(domainEvent);
    }
}

/// <summary>Exposes <c>RaiseDomainEvent</c> so the inherited hook can be exercised.</summary>
internal sealed class SampleAuditableAggregate(Guid id) : AuditableAggregateRoot<Guid>(id)
{
    public void Raise(IDomainEvent domainEvent) => RaiseDomainEvent(domainEvent);
}

internal sealed record SampleAuditableDomainEvent(DateTimeOffset OccurredOn) : IDomainEvent;

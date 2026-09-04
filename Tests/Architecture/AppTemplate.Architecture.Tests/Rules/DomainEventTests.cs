using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Architecture.Tests.Fixtures;
using AppTemplate.Domain.Common.Events;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

/// <summary>
/// Whether a domain event is listened to is a decision, and this is where it is recorded.
/// <para>
/// The rule exists because the opposite happened: three files in the <c>Files</c> feature explained,
/// at length and convincingly, that a deletion event had a consumer which reclaimed a file's bytes
/// promptly — and no such consumer had ever been written. Every test was green, because nothing in
/// this repository related the events that are raised to the consumers that exist. The prose was the
/// only thing asserting it, and prose is not checked.
/// </para>
/// </summary>
public sealed class DomainEventTests
{
    /// <summary>
    /// The events nothing listens to, on purpose. An event with no consumer is a perfectly good
    /// thing — it is a fact the domain publishes for a future reader — but it has to be a decision
    /// somebody made rather than a consumer somebody forgot, and this list is where the difference
    /// is stated.
    /// </summary>
    private static readonly string[] _deliberatelyUnconsumed =
    [
        // A creation fact, published for an audit or a projection a project may want. Nothing in the
        // template needs it.
        "TodoListCreatedDomainEvent",

        // The counterpart of completion. Reopening does not resurrect a cancelled reminder: firing
        // re-reads the item's state anyway, so a consumer here would duplicate that check.
        "TodoItemReopenedDomainEvent",

        // Firing is already recorded on the aggregate and counted by the worker's own log. A
        // consumer would be a second place to keep in step for no gain.
        "ReminderFiredDomainEvent",

        // The hook a project adds derivative generation to — thumbnails, previews, transcoding.
        // Deliberately empty here: the template has no image pipeline, and an unused consumer that
        // did nothing would be worse documentation than this line.
        "StoredFileMadeAvailableDomainEvent",

        // A refusal, published for a project that wants to notify the owner or raise an alert.
        // Nothing consumes it, and nothing that has to may be built on it: the state is committed
        // on the row before this is dispatched, so the file is already unservable by then — a
        // consumer here could only ever be a notification, never the thing that makes the refusal
        // true.
        "StoredFileQuarantinedDomainEvent",
    ];

    [Fact]
    public void EveryDomainEvent_IsEitherConsumedOrListedAsUnconsumed()
    {
        var events = ArchitectureAssemblies.Domain
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, IsNested: false })
            .Where(type => typeof(IDomainEvent).IsAssignableFrom(type))
            .ToList();

        events.Count.ShouldBeGreaterThanOrEqualTo(
            5,
            "Far fewer domain events were found than this template raises, so this rule is reading " +
            "the wrong assembly and passing for the wrong reason.");

        var consumed = ArchitectureAssemblies.Application
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .SelectMany(type => type.GetInterfaces())
            .Where(contract => contract.IsGenericType
                && contract.GetGenericTypeDefinition() == typeof(IDomainEventConsumer<>))
            .Select(contract => contract.GetGenericArguments()[0].Name)
            .ToHashSet(StringComparer.Ordinal);

        consumed.ShouldNotBeEmpty(
            "No consumer was found at all, so every event would look deliberately unconsumed and " +
            "this rule would be checking a list against nothing.");

        var unaccounted = events
            .Select(domainEvent => domainEvent.Name)
            .Where(name => !consumed.Contains(name))
            .Where(name => !_deliberatelyUnconsumed.Contains(name, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        unaccounted.ShouldBeEmpty(
            "An event with no consumer is fine, but it has to be written down as a decision. Add " +
            "the consumer, or add the event to the list above with the reason nothing listens to " +
            "it — and check that no comment anywhere claims a consumer that does not exist.");
    }

    /// <summary>
    /// The list cannot outlive what it describes. An entry naming an event that has since gained a
    /// consumer — or been deleted — would be documenting a decision nobody is making any more, and
    /// would quietly excuse the next event that arrives under the same name.
    /// </summary>
    [Fact]
    public void NoEventIsListedAsUnconsumed_WhileSomethingConsumesIt()
    {
        var eventNames = ArchitectureAssemblies.Domain
            .GetTypes()
            .Where(type => typeof(IDomainEvent).IsAssignableFrom(type))
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        var consumed = ArchitectureAssemblies.Application
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .SelectMany(type => type.GetInterfaces())
            .Where(contract => contract.IsGenericType
                && contract.GetGenericTypeDefinition() == typeof(IDomainEventConsumer<>))
            .Select(contract => contract.GetGenericArguments()[0].Name)
            .ToHashSet(StringComparer.Ordinal);

        _deliberatelyUnconsumed
            .Where(name => !eventNames.Contains(name) || consumed.Contains(name))
            .Order(StringComparer.Ordinal)
            .ShouldBeEmpty(
                "This entry no longer describes anything: the event was renamed or removed, or it " +
                "has gained a consumer. Either way the line is now excusing nothing and should go.");
    }
}

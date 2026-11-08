using AppTemplate.Application.Core.Common.Events;
using AppTemplate.Architecture.Tests.Fixtures;
using AppTemplate.Domain.Core.Common.Events;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class DomainEventTests
{
    private static readonly string[] _deliberatelyUnconsumed =
    [
        //#if (todoLists)
        "TodoListCreatedDomainEvent",
        "TodoItemReopenedDomainEvent",
        //#endif
        //#if (reminders)
        "ReminderFiredDomainEvent",
        //#endif
        //#if (files)
        "StoredFileMadeAvailableDomainEvent",
        "StoredFileQuarantinedDomainEvent",
        //#endif
    ];

    [Fact]
    public void EveryDomainEvent_IsEitherConsumedOrListedAsUnconsumed()
    {
        var events = ArchitectureAssemblies.Domain
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, IsNested: false })
            .Where(type => typeof(IDomainEvent).IsAssignableFrom(type))
            .ToList();

        //#if (aggregates)
        events.Count.ShouldBeGreaterThanOrEqualTo(
            3,
            "Far fewer domain events were found than this template raises, so this rule is reading " +
            "the wrong assembly and passing for the wrong reason.");

        //#endif
        var consumed = ArchitectureAssemblies.ApplicationLayer
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .SelectMany(type => type.GetInterfaces())
            .Where(contract => contract.IsGenericType
                && contract.GetGenericTypeDefinition() == typeof(IDomainEventConsumer<>))
            .Select(contract => contract.GetGenericArguments()[0].Name)
            .ToHashSet(StringComparer.Ordinal);

        //#if (aggregates)
        consumed.ShouldNotBeEmpty(
            "No consumer was found at all, so every event would look deliberately unconsumed and " +
            "this rule would be checking a list against nothing.");

        //#endif
        Unaccounted(events.Select(domainEvent => domainEvent.Name), consumed, _deliberatelyUnconsumed).ShouldBeEmpty(
            "An event with no consumer is fine, but it has to be written down as a decision. Add " +
            "a class implementing IDomainEventConsumer<TheEvent> in the application layer, or add " +
            "the event's name to _deliberatelyUnconsumed in this file with the reason nothing " +
            "listens to it — and check that no comment anywhere claims a consumer that does not exist.");
    }

    [Fact]
    public void NoEventIsListedAsUnconsumed_WhileSomethingConsumesIt()
    {
        var eventNames = ArchitectureAssemblies.Domain
            .GetTypes()
            .Where(type => typeof(IDomainEvent).IsAssignableFrom(type))
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        var consumed = ArchitectureAssemblies.ApplicationLayer
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .SelectMany(type => type.GetInterfaces())
            .Where(contract => contract.IsGenericType
                && contract.GetGenericTypeDefinition() == typeof(IDomainEventConsumer<>))
            .Select(contract => contract.GetGenericArguments()[0].Name)
            .ToHashSet(StringComparer.Ordinal);

        StaleEntries(_deliberatelyUnconsumed, eventNames, consumed).ShouldBeEmpty(
            "This entry no longer describes anything: the event was renamed or removed, or it " +
            "has gained a consumer. Either way the line is now excusing nothing: delete each name " +
            "listed here from _deliberatelyUnconsumed in this file.");
    }

    [Fact]
    public void TheAccounting_IsSensitive_AndSeesAnEventNobodyConsumesAndAStaleExcuse()
    {
        string[] excused = ["Quiet", "Renamed", "NowConsumed"];
        HashSet<string> consumed = ["Heard", "NowConsumed"];

        Unaccounted(["Heard", "Quiet", "Orphan"], consumed, excused).ShouldBe(["Orphan"]);

        StaleEntries(excused, new HashSet<string> { "Heard", "Quiet", "NowConsumed" }, consumed)
            .ShouldBe(["NowConsumed", "Renamed"]);
    }

    private static List<string> Unaccounted(IEnumerable<string> events, HashSet<string> consumed, IEnumerable<string> excused) =>
        [.. events
            .Where(name => !consumed.Contains(name))
            .Where(name => !excused.Contains(name, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)];

    private static List<string> StaleEntries(IEnumerable<string> excused, HashSet<string> eventNames, HashSet<string> consumed) =>
        [.. excused
            .Where(name => !eventNames.Contains(name) || consumed.Contains(name))
            .Order(StringComparer.Ordinal)];
}

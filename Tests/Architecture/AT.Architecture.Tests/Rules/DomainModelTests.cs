using System.Reflection;
using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using AppTemplate.Domain.Core.Common.Events;
using AppTemplate.Domain.Core.Common.Primitives;
using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class DomainModelTests
{
    private const string _eventsNamespace = @"^AppTemplate\.Domain\.Features\.[A-Za-z]+\.Model\.Events$";

    private static readonly string[] _expectedEntities =
    [
        //#if (todoLists)
        "TodoList",
        "TodoItem",
        //#endif
        //#if (files)
        "StoredFile",
        //#endif
    ];

    [Fact]
    public void AggregateRoots_AreSealed()
    {
        var roots = ArchitectureAssemblies.Domain
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(typeof(IAggregateRoot).IsAssignableFrom)
            .ToList();

        //#if (aggregates)
        roots.ShouldNotBeEmpty(
            "No concrete IAggregateRoot was found in AppTemplate.Domain. Either the marker is gone or this " +
            "discovery has stopped working; either way the rule below would pass for nothing.");

        //#endif
        Unsealed(roots).ShouldBeEmpty(
            "An aggregate root is a consistency boundary. Subclassing one lets a derived type " +
            "add state the root's own invariants do not cover. Declare each type named here " +
            "'sealed', or stop it being an IAggregateRoot.");
    }

    private static List<string> Unsealed(IEnumerable<Type> roots) =>
        [.. roots.Where(type => !type.IsSealed).Select(type => type.FullName ?? type.Name)];

    [Fact]
    public void Entities_HaveNoPubliclySettableState()
    {
        PubliclySettable(DomainEntities()).ShouldBeEmpty(
            "A domain entity's state is changed by its own behaviour, not by assignment. Audit " +
            "values are written through the explicit IAuditable implementation, which is why those " +
            "members are declared on the interface rather than as public setters. Make each setter " +
            "named here private, or an init accessor, and add a method that changes the state.");
    }

    private static List<string> PubliclySettable(IEnumerable<Type> entities)
    {
        var offenders = new List<string>();

        foreach (var entity in entities)
        {
            foreach (var declaring in WithBases(entity))
            {
                foreach (var property in declaring.GetProperties(_declaredState))
                {
                    var setter = property.SetMethod;

                    if (setter is null || !setter.IsPublic || TypeFacts.IsInitOnly(setter))
                    {
                        continue;
                    }

                    string offence = $"{property.DeclaringType?.Name}.{property.Name} has a public setter";

                    if (!offenders.Contains(offence, StringComparer.Ordinal))
                    {
                        offenders.Add(offence);
                    }
                }
            }
        }

        return offenders;
    }

    [Fact]
    public void Entities_HaveNoPublicFields()
    {
        PublicFields(DomainEntities()).ShouldBeEmpty(
            "A public field bypasses every rule the type enforces, and cannot later be made to " +
            "enforce one without a breaking change. Turn each field named here into a property with " +
            "a private or init setter.");
    }

    private static List<string> PublicFields(IEnumerable<Type> entities)
    {
        var offenders = new List<string>();

        foreach (var entity in entities)
        {
            foreach (var declaring in WithBases(entity))
            {
                foreach (var field in declaring.GetFields(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    string offence = $"{field.DeclaringType?.Name}.{field.Name} is a public field";

                    if (!offenders.Contains(offence, StringComparer.Ordinal))
                    {
                        offenders.Add(offence);
                    }
                }
            }
        }

        return offenders;
    }

    [Fact]
    public void ValueObjectsAndDomainEvents_AreImmutable()
    {
        var records = ArchitectureAssemblies.Domain
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, IsPublic: true })
            .Where(TypeFacts.IsRecord)
            .ToList();

        //#if (aggregates)
        records.ShouldNotBeEmpty(
            "No record types were found in AppTemplate.Domain. Value objects and domain events are modelled " +
            "as records; if that has changed, this rule needs rewriting rather than deleting.");

        //#endif
        SettableAfterConstruction(records).ShouldBeEmpty(
            "A value object or a domain event is a value: settable after construction, its equality " +
            "and every rule its factory enforced become advisory. Declare each property named here " +
            "with 'init' or no setter.");
    }

    private static List<string> SettableAfterConstruction(IEnumerable<Type> records)
    {
        var offenders = new List<string>();

        foreach (var record in records)
        {
            foreach (var property in record.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var setter = property.SetMethod;

                if (setter is null || TypeFacts.IsInitOnly(setter))
                {
                    continue;
                }

                offenders.Add($"{record.Name}.{property.Name} is settable after construction");
            }
        }

        return offenders;
    }

    [Fact]
    public void DomainEvents_AreSealedAndImplementTheMarker()
    {
        var events = Types.InAssembly(ArchitectureAssemblies.Domain)
            .That()
            .ResideInNamespaceMatching(_eventsNamespace)
            .And()
            .AreClasses();

        //#if (aggregates)
        RuleAssertions.RequireTypes(events, "a type in a AppTemplate.Domain '...Events' namespace");

        //#endif
        events.Should()
            .BeSealed()
            .And()
            .ImplementInterface(typeof(IDomainEvent))
            .GetResult()
            .ShouldHold(
                "Everything in an Events namespace is a sealed domain event implementing " +
                "IDomainEvent. The persistence dispatcher finds consumers through that marker, so a " +
                "type that misses it is collected by nothing and silently never dispatched. Declare " +
                "each type named here 'sealed' and implementing IDomainEvent, or move it out of the " +
                "Events namespace if it is not an event.");
    }

    private const BindingFlags _declaredState =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private static IEnumerable<Type> WithBases(Type entity)
    {
        for (var type = entity; type is not null && type != typeof(object); type = type.BaseType)
        {
            yield return type;
        }
    }

    private static List<Type> DomainEntities()
    {
        var entities = ArchitectureAssemblies.Domain
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => TypeFacts.DerivesFromOpenGeneric(type, typeof(Entity<>)))
            .ToList();

        //#if (aggregates)
        entities.ShouldNotBeEmpty("No Entity<TId> descendants were found in AppTemplate.Domain.");

        //#endif
        foreach (string expected in _expectedEntities)
        {
            entities.Select(entity => entity.Name)
                .ShouldContain(
                    expected,
                    $"'{expected}' is no longer a concrete Entity<TId> in AppTemplate.Domain. Either the " +
                    "model changed or the discovery in this test has stopped working.");
        }

        return entities;
    }

    [Fact]
    public void TheSealedRule_IsSensitive_AndSeesAnOpenRoot() =>
        Unsealed([typeof(SealedState), typeof(OpenState)]).ShouldBe([typeof(OpenState).FullName!]);

    [Fact]
    public void TheStateRule_IsSensitive_AndSeesAPublicSetterOnTheTypeOrItsBase()
    {
        PubliclySettable([typeof(DerivedState)]).ShouldBe(
            [$"{nameof(OpenState)}.{nameof(OpenState.Assigned)} has a public setter"]);

        PubliclySettable([typeof(SealedState)]).ShouldBeEmpty();
    }

    [Fact]
    public void TheFieldRule_IsSensitive_AndSeesAPublicFieldOnTheTypeOrItsBase()
    {
        PublicFields([typeof(DerivedState)]).ShouldBe(
            [$"{nameof(OpenState)}.{nameof(OpenState.Exposed)} is a public field"]);

        PublicFields([typeof(SealedState)]).ShouldBeEmpty();
    }

    [Fact]
    public void TheImmutabilityRule_IsSensitive_AndSeesASetterOnARecordButNotAnInit()
    {
        SettableAfterConstruction([typeof(MutableValue), typeof(FrozenValue), typeof(InitOnlyValue)])
            .ShouldBe([$"{nameof(MutableValue)}.{nameof(MutableValue.Amount)} is settable after construction"]);
    }

    [Theory]
    [InlineData("AppTemplate.Domain.Features.Orders.Model.Events", true)]
    [InlineData("AppTemplate.Domain.Features.Orders.Model", false)]
    [InlineData("AppTemplate.Domain.Features.Orders.Model.Events.Deeper", false)]
    [InlineData("AppTemplate.Domain.Core.Common.Events", false)]
    public void TheEventsNamespace_IsSensitive_AndMatchesOnlyAFeaturesEventsFolder(string @namespace, bool matches) =>
        Regex.IsMatch(@namespace, _eventsNamespace, RegexOptions.None, TimeSpan.FromSeconds(5)).ShouldBe(matches);

    private sealed class SealedState
    {
        public int Read { get; private set; }

        public int Initialised { get; init; }
    }

    private class OpenState
    {
        public int Assigned { get; set; }

        public int Exposed = 1;
    }

    private sealed class DerivedState : OpenState;

    private sealed record MutableValue
    {
        public int Amount { get; set; }
    }

    private sealed record FrozenValue(int Amount);

    private sealed record InitOnlyValue
    {
        public int Amount { get; init; }
    }
}

using System.Reflection;
using AppTemplate.Architecture.Tests.Fixtures;
using AppTemplate.Domain.Common.Events;
using AppTemplate.Domain.Common.Primitives;
using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

/// <summary>
/// Encapsulation of the domain model: state changes go through behaviour, never through a setter.
/// <para>
/// NetArchTest can express the shape of a type — sealed, abstract, which interface it implements —
/// but it has no condition for the accessibility of a property's setter, which is the thing that
/// actually decides whether an invariant can be bypassed. Those rules are therefore written with
/// reflection over the loaded assembly, which is if anything more precise: it sees the accessor's
/// real accessibility and can tell an <c>init</c> accessor from a settable one.
/// </para>
/// </summary>
public sealed class DomainModelTests
{
    /// <summary>
    /// Entities that must be present. If the model is refactored these change, but the set must
    /// never become empty — a reflection rule over nothing passes.
    /// </summary>
    private static readonly string[] _expectedEntities = ["TodoList", "TodoItem"];

    /// <summary>
    /// Written with reflection because NetArchTest's <c>ImplementInterface</c> only sees interfaces a
    /// type declares itself. <c>TodoList</c> reaches <see cref="IAggregateRoot"/> through
    /// <c>AggregateRoot&lt;TId&gt;</c>, so the rule engine matches nothing — and a rule that matches
    /// nothing passes, which is precisely the failure mode this project is built to avoid.
    /// </summary>
    [Fact]
    public void AggregateRoots_AreSealed()
    {
        var roots = ArchitectureAssemblies.Domain
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(typeof(IAggregateRoot).IsAssignableFrom)
            .ToList();

        roots.ShouldNotBeEmpty(
            "No concrete IAggregateRoot was found in AppTemplate.Domain. Either the marker is gone or this " +
            "discovery has stopped working; either way the rule below would pass for nothing.");

        roots.Where(type => !type.IsSealed)
            .Select(type => type.FullName ?? type.Name)
            .ShouldBeEmpty(
                "An aggregate root is a consistency boundary. Subclassing one lets a derived type " +
                "add state the root's own invariants do not cover.");
    }

    /// <summary>
    /// A public setter on an audit or ownership field would let application code assign
    /// <c>OwnerId</c> — the value every authorisation check reads — and forge <c>CreatedBy</c>.
    /// </summary>
    [Fact]
    public void Entities_HaveNoPubliclySettableState()
    {
        var entities = DomainEntities();
        var offenders = new List<string>();

        foreach (var entity in entities)
        {
            foreach (var property in entity.GetProperties(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                var setter = property.SetMethod;

                if (setter is null || !setter.IsPublic || TypeFacts.IsInitOnly(setter))
                {
                    continue;
                }

                offenders.Add($"{property.DeclaringType?.Name}.{property.Name} has a public setter");
            }
        }

        offenders.ShouldBeEmpty(
            "A domain entity's state is changed by its own behaviour, not by assignment. Audit " +
            "values are written through the explicit IAuditable implementation, which is why those " +
            "members are declared on the interface rather than as public setters.");
    }

    [Fact]
    public void Entities_HaveNoPublicFields()
    {
        var entities = DomainEntities();
        var offenders = new List<string>();

        foreach (var entity in entities)
        {
            foreach (var field in entity.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                offenders.Add($"{field.DeclaringType?.Name}.{field.Name} is a public field");
            }
        }

        offenders.ShouldBeEmpty(
            "A public field bypasses every rule the type enforces, and cannot later be made to " +
            "enforce one without a breaking change.");
    }

    /// <summary>
    /// Value objects and domain events are modelled as records here, so their state is fixed by the
    /// constructor. A plain settable property on one of them would make a supposedly immutable value
    /// mutable, and would break the equality the rest of the model relies on.
    /// </summary>
    [Fact]
    public void ValueObjectsAndDomainEvents_AreImmutable()
    {
        var records = ArchitectureAssemblies.Domain
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, IsPublic: true })
            .Where(TypeFacts.IsRecord)
            .ToList();

        records.ShouldNotBeEmpty(
            "No record types were found in AppTemplate.Domain. Value objects and domain events are modelled " +
            "as records; if that has changed, this rule needs rewriting rather than deleting.");

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

        offenders.ShouldBeEmpty(
            "A value object or a domain event is a value: settable after construction, its equality " +
            "and every rule its factory enforced become advisory.");
    }

    [Fact]
    public void DomainEvents_AreSealedAndImplementTheMarker()
    {
        var events = Types.InAssembly(ArchitectureAssemblies.Domain)
            .That()
            .ResideInNamespaceStartingWith("AppTemplate.Domain.Features.TodoLists.Events")
            .And()
            .AreClasses();

        RuleAssertions.RequireTypes(events, "a type in a AppTemplate.Domain '...Events' namespace");

        events.Should()
            .BeSealed()
            .And()
            .ImplementInterface(typeof(IDomainEvent))
            .GetResult()
            .ShouldHold(
                "Everything in an Events namespace is a sealed domain event implementing " +
                "IDomainEvent. The persistence dispatcher finds consumers through that marker, so a " +
                "type that misses it is collected by nothing and silently never dispatched.");
    }

    /// <summary>
    /// The entities and aggregates of the domain: concrete classes deriving from
    /// <c>Entity&lt;TId&gt;</c>. Asserted non-empty, and asserted to contain the entities the model
    /// is known to have, so the reflection rules above cannot pass by finding nothing.
    /// </summary>
    private static List<Type> DomainEntities()
    {
        var entities = ArchitectureAssemblies.Domain
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => TypeFacts.DerivesFromOpenGeneric(type, typeof(Entity<>)))
            .ToList();

        entities.ShouldNotBeEmpty("No Entity<TId> descendants were found in AppTemplate.Domain.");

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
}

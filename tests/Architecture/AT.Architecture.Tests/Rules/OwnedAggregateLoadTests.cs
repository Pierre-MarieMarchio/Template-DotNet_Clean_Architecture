using System.Reflection;
using AppTemplate.Architecture.Tests.Fixtures;
using AppTemplate.Domain.Core.Common.Abstractions;
using AppTemplate.Domain.Core.Common.Primitives;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class OwnedAggregateLoadTests
{
    [Fact]
    public void ALoadOfOneOwnedAggregate_TakesItsOwner()
    {
        var loads = LoadsOfOneOwnedAggregate();

        //#if (todoLists || files)
        loads.ShouldNotBeEmpty(
            "No domain port returns one IOwnedAggregate. Either the marker is gone or this discovery has " +
            "stopped working; either way the rule below would pass for nothing.");

        //#endif
        WithoutAnOwner(loads).ShouldBeEmpty(
                "Another caller's record answers 404 because the owner is in the query (ADR 0009). A port " +
                "that loads one owned aggregate takes the caller's UserId, so its adapter filters by owner " +
                "and id in the same statement and never reads another user's row. A load with no caller " +
                "behind it, such as a sweep, reads a batch under a name of its own. Add a UserId " +
                "parameter to each load named here and filter by it in the adapter's query.");
    }

    [Fact]
    public void TheRule_IsSensitive_AndSeesALoadWithNoOwnerButNotOneWithAnOwnerOrABatch()
    {
        var loads = LoadsOfOneOwnedAggregate([typeof(IProbeRepository)]);

        loads.Select(load => load.Name).ShouldBe([nameof(IProbeRepository.ByOwnerAndId), nameof(IProbeRepository.ById)], ignoreOrder: true);
        WithoutAnOwner(loads).ShouldBe([$"{nameof(IProbeRepository)}.{nameof(IProbeRepository.ById)}"]);
    }

    private static List<string> WithoutAnOwner(IEnumerable<MethodInfo> loads) =>
        [.. loads
            .Where(load => !Array.Exists(load.GetParameters(), parameter => parameter.ParameterType == typeof(UserId)))
            .Select(load => $"{load.DeclaringType?.Name}.{load.Name}")];

    private static List<MethodInfo> LoadsOfOneOwnedAggregate() =>
        LoadsOfOneOwnedAggregate(ArchitectureAssemblies.Domain.GetTypes());

    private static List<MethodInfo> LoadsOfOneOwnedAggregate(IEnumerable<Type> types) =>
        [.. types
            .Where(type => type.IsInterface)
            .SelectMany(port => port.GetMethods())
            .Where(method => LoadedType(method.ReturnType) is { } loaded
                && typeof(IOwnedAggregate).IsAssignableFrom(loaded))];

    private sealed record Owned(UserId OwnerId) : IOwnedAggregate;

    private interface IProbeRepository
    {
        Task<Owned?> ByOwnerAndId(UserId owner, Guid id);

        Task<Owned?> ById(Guid id);

        Task<List<Owned>> Sweep();

        Task<int> Count(Guid id);
    }

    private static Type? LoadedType(Type returned) =>
        returned.IsGenericType
        && (returned.GetGenericTypeDefinition() == typeof(Task<>)
            || returned.GetGenericTypeDefinition() == typeof(ValueTask<>))
            ? returned.GetGenericArguments()[0]
            : null;
}

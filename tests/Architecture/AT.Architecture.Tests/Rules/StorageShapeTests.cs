using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class StorageShapeTests
{
    private static readonly string[] _softDeleteFlags =
    [
        "Deleted",
        "DeletedAt",
        "DeletedOn",
        "DeletedUtc",
        "IsDeleted",
        "IsRemoved",
        "SoftDeleted",
    ];

    [Fact]
    public void NoPersistenceRecord_CarriesADeletedFlag()
    {
        var records = ArchitectureAssemblies.Persistence
            .GetTypes()
            .Where(type => type is { IsClass: true, IsNested: false })
            .Where(type => type.Namespace?.EndsWith(".Models", StringComparison.Ordinal) == true
                || type.Name.EndsWith("Record", StringComparison.Ordinal))
            .ToList();

        records.Count.ShouldBeGreaterThanOrEqualTo(
            1,
            "Almost no persistence record was found, so this rule is checking an empty set and " +
            "passing for the wrong reason.");

        DeletedFlagsOn(records).ShouldBeEmpty(
            "A row is either there or it is not. A deleted flag makes every query responsible for " +
            "remembering it, and the one that forgets serves data that was meant to be gone. Remove " +
            "each property named here and delete the row instead; keep a record of the deletion in " +
            "an audit table if one is needed.");
    }

    private static List<string> DeletedFlagsOn(IEnumerable<Type> records) =>
        [.. records
            .SelectMany(record => record
                .GetProperties()
                .Where(property => _softDeleteFlags.Contains(property.Name, StringComparer.Ordinal))
                .Select(property => $"{record.FullName}.{property.Name}"))
            .Order(StringComparer.Ordinal)];

    [Fact]
    public void NoApplicationPort_ExposesAQueryable()
    {
        ApplicationPorts.All.Count.ShouldBeGreaterThanOrEqualTo(
            7,
            "Almost no port was discovered, so this rule is checking an empty set.");

        QueryableMembersOf(ApplicationPorts.All).ShouldBeEmpty(
            "A port hands back an answer. Handing back a query tree moves the database's evaluation " +
            "rules into every caller. Make each method named here return the materialised answer " +
            "(a list, a page or a single item) and keep the query inside the adapter.");
    }

    private static List<string> QueryableMembersOf(IEnumerable<Type> ports) =>
        [.. ports
            .SelectMany(port => port
                .GetMethods()
                .Where(method => NamesAQueryable(method.ReturnType)
                    || method.GetParameters().Any(parameter => NamesAQueryable(parameter.ParameterType)))
                .Select(method => $"{port.FullName}.{method.Name}"))
            .Order(StringComparer.Ordinal)];

    [Fact]
    public void TheDeletedFlagRule_IsSensitive_AndSeesEverySpellingButNotOrdinaryProperties() =>
        DeletedFlagsOn([typeof(FlaggedRecord), typeof(PlainRecord)]).ShouldBe(
            [
                $"{typeof(FlaggedRecord).FullName}.{nameof(FlaggedRecord.DeletedAt)}",
                $"{typeof(FlaggedRecord).FullName}.{nameof(FlaggedRecord.IsDeleted)}",
            ]);

    [Fact]
    public void TheQueryableRule_IsSensitive_AndSeesAReturnedAWrappedAndAnAcceptedQueryable() =>
        QueryableMembersOf([typeof(IProbePort)]).ShouldBe(
            [
                $"{typeof(IProbePort).FullName}.{nameof(IProbePort.Accepts)}",
                $"{typeof(IProbePort).FullName}.{nameof(IProbePort.Returns)}",
                $"{typeof(IProbePort).FullName}.{nameof(IProbePort.Wraps)}",
            ]);

    private sealed class FlaggedRecord
    {
        public DateTimeOffset? DeletedAt { get; set; }

        public bool IsDeleted { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private sealed class PlainRecord
    {
        public string Title { get; set; } = string.Empty;
    }

    private interface IProbePort
    {
        IQueryable<int> Returns();

        Task<IQueryable<string>> Wraps();

        void Accepts(IQueryable<int> query);

        Task<List<int>> Materialised();
    }

    private static bool NamesAQueryable(Type type)
    {
        if (type.Name.StartsWith("IQueryable", StringComparison.Ordinal))
        {
            return true;
        }

        return type.IsGenericType
            && type.GetGenericArguments().Any(NamesAQueryable);
    }
}

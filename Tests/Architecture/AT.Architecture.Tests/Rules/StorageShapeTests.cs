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

        var offenders = records
            .SelectMany(record => record
                .GetProperties()
                .Where(property => _softDeleteFlags.Contains(property.Name, StringComparer.Ordinal))
                .Select(property => $"{record.FullName}.{property.Name}"))
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.ShouldBeEmpty(
            "A row is either there or it is not. A deleted flag makes every query responsible for " +
            "remembering it, and the one that forgets serves data that was meant to be gone.");
    }

    [Fact]
    public void NoApplicationPort_ExposesAQueryable()
    {
        ApplicationPorts.All.Count.ShouldBeGreaterThanOrEqualTo(
            7,
            "Almost no port was discovered, so this rule is checking an empty set.");

        var offenders = ApplicationPorts.All
            .SelectMany(port => port
                .GetMethods()
                .Where(method => NamesAQueryable(method.ReturnType)
                    || method.GetParameters().Any(parameter => NamesAQueryable(parameter.ParameterType)))
                .Select(method => $"{port.FullName}.{method.Name}"))
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.ShouldBeEmpty(
            "A port hands back an answer. Handing back a query tree moves the database's evaluation " +
            "rules into every caller.");
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

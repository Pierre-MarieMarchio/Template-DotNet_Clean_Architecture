using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

/// <summary>
/// Two shapes storage is not allowed to take, each of which is invisible until the day it costs
/// something: a row that is deleted but still there, and a contract that hands out a query instead
/// of an answer.
/// </summary>
public sealed class StorageShapeTests
{
    /// <summary>
    /// The property names a soft delete arrives under. Matching by name is the point: a soft delete
    /// is a convention, not a type, and it is the convention that has to be refused.
    /// </summary>
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

    /// <summary>
    /// <c>DELETE</c> removes rows. A deleted flag puts a predicate in every query, where one
    /// forgotten <c>Where</c> is a data leak rather than a bug, and it answers a retention question
    /// that belongs to the log pipeline instead.
    /// </summary>
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
            5,
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

    /// <summary>
    /// A contract that returns <c>IQueryable</c> has not hidden the database, it has published it:
    /// the caller composes the query, the caller decides when it runs, and the caller is where the
    /// lazy load happens. A port returns answers.
    /// </summary>
    [Fact]
    public void NoApplicationPort_ExposesAQueryable()
    {
        ApplicationPorts.All.Count.ShouldBeGreaterThanOrEqualTo(
            20,
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

using System.Reflection;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

/// <summary>
/// Four words name the four ways this template reaches storage, and each promises something
/// different about what is behind the contract and about who may hold it.
/// <list type="bullet">
/// <item><c>Repository</c> loads an aggregate, so it is declared in <c>AppTemplate.Domain</c>
/// beside the aggregate it loads.</item>
/// <item><c>Queries</c> projects rows onto a DTO without materialising an aggregate.</item>
/// <item><c>Store</c> is an application port for storage with no aggregate behind it — an
/// idempotency claim.</item>
/// <item><c>Table</c> is row access to one table, declared inside the persistence project and
/// reached only by a sibling infrastructure module — a refresh-token grant.</item>
/// </list>
/// <para>
/// <c>Store</c> named the last two of those at once until the word was split, which nothing
/// noticed for a month because the distinction lived in prose. The rules below are what stops it
/// happening again, in both directions: a <c>Store</c> that starts naming an aggregate has become
/// a <c>Repository</c>, and a <c>Table</c> that a use case starts depending on has become a port
/// and needs declaring where ports are declared.
/// </para>
/// </summary>
public sealed class StorageVocabularyTests
{
    private const string _domainFeatureNamespace = "AppTemplate.Domain.Features";

    /// <summary>
    /// What separates a <c>Store</c> from a <c>Repository</c>: there is no aggregate behind it. A
    /// signature naming a domain entity is the moment that stops being true.
    /// </summary>
    [Fact]
    public void NoStoreContract_NamesAnAggregate()
    {
        var stores = ContractsNamed("Store");

        stores.Count.ShouldBeGreaterThanOrEqualTo(
            1,
            "No Store contract was found at all, so this rule is checking an empty set and passing " +
            "for the wrong reason.");

        Offenders(stores).ShouldBeEmpty(
            "A Store is storage with no aggregate behind it. One whose signature names a domain " +
            "entity is a Repository, and a Repository is declared in AppTemplate.Domain beside the " +
            "aggregate it loads.");
    }

    /// <summary>
    /// The same rule for the word that replaced <c>Store</c> on the persistence side. A
    /// <c>Table</c> is rows in and rows out; the moment it speaks in aggregates it has stopped
    /// being one.
    /// </summary>
    [Fact]
    public void NoTableContract_NamesAnAggregate()
    {
        var tables = ContractsNamed("Table");

        tables.Count.ShouldBeGreaterThanOrEqualTo(
            1,
            "No Table contract was found at all, so this rule is checking an empty set.");

        Offenders(tables).ShouldBeEmpty(
            "A Table is row access to one table. One whose signature names a domain entity is a " +
            "Repository wearing a persistence word.");
    }

    /// <summary>
    /// The distinction that made the rename worth doing: a <c>Table</c> is declared where its rows
    /// are, and no use case knows it exists. One that appears in Application has become a port, and
    /// a port is a <c>Store</c>, a <c>Repository</c> or a <c>Queries</c> — not a table.
    /// </summary>
    [Fact]
    public void NoTableContract_IsDeclaredInOrReachedFromTheApplicationLayer()
    {
        var declaredOutsidePersistence = ContractsNamed("Table")
            .Where(table => table.Assembly != ArchitectureAssemblies.Persistence)
            .Select(table => table.FullName ?? table.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        declaredOutsidePersistence.ShouldBeEmpty(
            "A Table contract belongs in the assembly that owns the rows. Declared anywhere else it " +
            "is a port that forgot to say so.");

        var ports = ApplicationPorts.All
            .Where(port => port.Name.EndsWith("Table", StringComparison.Ordinal))
            .Select(port => port.FullName ?? port.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        ports.ShouldBeEmpty(
            "A use case depending on a Table means the contract crossed into the application's " +
            "vocabulary, where the word for storage with no aggregate is Store.");
    }

    /// <summary>
    /// The half a rename would break first: whatever assembly an implementation lives in, the word
    /// <c>Repository</c> only ever appears on a contract declared in the Domain.
    /// </summary>
    [Fact]
    public void EveryRepositoryContract_IsDeclaredInTheDomain()
    {
        var misplaced = new[]
            {
                ArchitectureAssemblies.Application,
                ArchitectureAssemblies.Persistence,
                ArchitectureAssemblies.IdentityInfrastructure,
                ArchitectureAssemblies.EmailInfrastructure,
                ArchitectureAssemblies.InMemoryInfrastructure,
            }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsInterface: true, IsNested: false })
            .Where(type => type.Name.EndsWith("Repository", StringComparison.Ordinal))
            .Select(type => type.FullName ?? type.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        misplaced.ShouldBeEmpty(
            "A repository contract speaks only in domain types, so it belongs in AppTemplate.Domain " +
            "beside the aggregate. A contract outside it that borrows the word promises an aggregate " +
            "it cannot name.");

        ApplicationPorts.DomainRepositories.Count.ShouldBeGreaterThanOrEqualTo(
            2,
            "No repository contract was found in the Domain either, so the assertion above proves " +
            "nothing about a word nobody is using.");
    }

    private static List<Type> ContractsNamed(string suffix) =>
        [.. new[] { ArchitectureAssemblies.Application, ArchitectureAssemblies.Persistence }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsInterface: true, IsNested: false })
            .Where(type => type.Name.EndsWith(suffix, StringComparison.Ordinal))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)];

    private static List<string> Offenders(IEnumerable<Type> contracts) =>
        [.. contracts
            .SelectMany(contract => contract
                .GetMethods()
                .Where(method => Mentioned(method).Any(IsDomainFeatureType))
                .Select(method => $"{contract.Name}.{method.Name}"))
            .Order(StringComparer.Ordinal)];

    private static IEnumerable<Type> Mentioned(MethodInfo method) =>
        method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType);

    private static bool IsDomainFeatureType(Type type)
    {
        if (type.Namespace?.StartsWith(_domainFeatureNamespace, StringComparison.Ordinal) == true)
        {
            return true;
        }

        return type.IsGenericType && type.GetGenericArguments().Any(IsDomainFeatureType);
    }
}

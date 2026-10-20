using System.Reflection;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class StorageVocabularyTests
{
    private const string _domainFeatureNamespace = "AppTemplate.Domain.Features";

    [Fact]
    public void NoStoreContract_NamesAnAggregate()
    {
        var stores = ContractsNamed("Store");

        //#if (notes || files)
        stores.Count.ShouldBeGreaterThanOrEqualTo(
            1,
            "No Store contract was found at all, so this rule is checking an empty set and passing " +
            "for the wrong reason.");

        //#endif
        Offenders(stores).ShouldBeEmpty(
            "A Store is storage with no aggregate behind it. One whose signature names a domain " +
            "entity is a Repository, and a Repository is declared in AppTemplate.Domain beside the " +
            "aggregate it loads.");
    }

    [Fact]
    public void NoTableContract_NamesAnAggregate()
    {
        var tables = ContractsNamed("Table");

        //#if (localAuth)
        tables.Count.ShouldBeGreaterThanOrEqualTo(
            1,
            "No Table contract was found at all, so this rule is checking an empty set.");

        //#endif
        Offenders(tables).ShouldBeEmpty(
            "A Table is row access to one table. One whose signature names a domain entity is a " +
            "Repository wearing a persistence word.");
    }

    [Fact]
    public void NoTableContract_IsDeclaredInOrReachedFromTheApplicationLayer()
    {
        var tables = ContractsNamed("Table");

        //#if (localAuth)
        tables.Count.ShouldBeGreaterThanOrEqualTo(
            1,
            "No Table contract was found at all, so this rule is checking an empty set.");

        //#endif
        var declaredOutsideARowOwner = tables
            .Where(table => !_rowOwners.Contains(table.Assembly))
            .Select(table => table.FullName ?? table.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        declaredOutsideARowOwner.ShouldBeEmpty(
            "A Table contract belongs in the module that owns the rows — the business one for a " +
            "business table, the authentication one for the grant table. Declared anywhere else it " +
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

    [Fact]
    public void EveryRepositoryContract_IsDeclaredInTheDomain()
    {
        var misplaced = new[]
            {
                ArchitectureAssemblies.Application,
                ArchitectureAssemblies.Persistence,
                //#if (localAuth)
                ArchitectureAssemblies.IdentityInfrastructure,
                //#endif
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

        //#if (aggregates)
        ApplicationPorts.DomainRepositories.Count.ShouldBeGreaterThanOrEqualTo(
            1,
            "No repository contract was found in the Domain either, so the assertion above proves " +
            "nothing about a word nobody is using.");
        //#endif
    }

    private static readonly Assembly[] _rowOwners =
    [
        ArchitectureAssemblies.Persistence,
        //#if (localAuth)
        ArchitectureAssemblies.IdentityInfrastructure,
        //#endif
    ];

    private static List<Type> ContractsNamed(string suffix) =>
        [.. _rowOwners.Prepend(ArchitectureAssemblies.Application)
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

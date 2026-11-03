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
            "aggregate it loads. Move each contract named here to the Repositories folder of its " +
            "aggregate in AppTemplate.Domain and rename it '...Repository', or take the entity out " +
            "of its signature.");
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
            "Repository wearing a persistence word. Declare it as a Repository in AppTemplate.Domain, " +
            "or take the entity out of its signature.");
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
            "is a port that forgot to say so. Move each contract named here into the persistence " +
            "project (or the authentication one for the grant table), or rename it if it is a port.");

        var ports = ApplicationPorts.All
            .Where(port => port.Name.EndsWith("Table", StringComparison.Ordinal))
            .Select(port => port.FullName ?? port.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        ports.ShouldBeEmpty(
            "A use case depending on a Table means the contract crossed into the application's " +
            "vocabulary, where the word for storage with no aggregate is Store. Rename each port " +
            "named here '...Store'.");
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
            .Where(type => IsContractNamed(type, "Repository"))
            .Select(type => type.FullName ?? type.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        misplaced.ShouldBeEmpty(
            "A repository contract speaks only in domain types, so it belongs in AppTemplate.Domain " +
            "beside the aggregate. A contract outside it that borrows the word promises an aggregate " +
            "it cannot name. Move each contract named here to AppTemplate.Domain, or rename it " +
            "'...Store' if no aggregate is behind it.");

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
            .Where(type => IsContractNamed(type, suffix))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)];

    private static bool IsContractNamed(Type type, string suffix) =>
        type is { IsInterface: true, IsNested: false }
        && type.Name.EndsWith(suffix, StringComparison.Ordinal);

    private static List<string> Offenders(IEnumerable<Type> contracts, string featureNamespace = _domainFeatureNamespace) =>
        [.. contracts
            .SelectMany(contract => contract
                .GetMethods()
                .Where(method => Mentioned(method).Any(type => IsDomainFeatureType(type, featureNamespace)))
                .Select(method => $"{contract.Name}.{method.Name}"))
            .Order(StringComparer.Ordinal)];

    private static IEnumerable<Type> Mentioned(MethodInfo method) =>
        method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType);

    private static bool IsDomainFeatureType(Type type, string featureNamespace)
    {
        if (type.Namespace?.StartsWith(featureNamespace, StringComparison.Ordinal) == true)
        {
            return true;
        }

        return type.IsGenericType && type.GetGenericArguments().Any(argument => IsDomainFeatureType(argument, featureNamespace));
    }

    [Fact]
    public void TheVocabularyRule_IsSensitive_AndSeesADomainTypeNamedInASignature()
    {
        string featureNamespace = typeof(StorageVocabularyTests).Namespace!;

        Offenders([typeof(IProbeStore)], featureNamespace).ShouldBe(
            [
                $"{nameof(IProbeStore)}.{nameof(IProbeStore.Accepts)}",
                $"{nameof(IProbeStore)}.{nameof(IProbeStore.Returns)}",
                $"{nameof(IProbeStore)}.{nameof(IProbeStore.Wraps)}",
            ]);

        Offenders([typeof(IProbeStore)]).ShouldBeEmpty();
    }

    [Fact]
    public void TheContractMatcher_IsSensitive_AndSeesATopLevelInterfaceByItsSuffixAlone()
    {
        IsContractNamed(typeof(IDisposable), "Disposable").ShouldBeTrue();
        IsContractNamed(typeof(IDisposable), "Repository").ShouldBeFalse();
        IsContractNamed(typeof(string), "String").ShouldBeFalse();
        IsContractNamed(typeof(INestedRepository), "Repository").ShouldBeFalse();
    }

    private interface INestedRepository;

    private sealed record Aggregate;

    private interface IProbeStore
    {
        Aggregate Returns();

        Task<List<Aggregate>> Wraps();

        void Accepts(Aggregate aggregate);

        Task<int> Counts();
    }
}

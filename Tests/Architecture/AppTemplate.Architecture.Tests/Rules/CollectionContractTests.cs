using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using AppTemplate.Application.Common.Collections;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

/// <summary>
/// What the sorting/filtering/paging surface guarantees about the shapes it hands between layers.
/// <para>
/// Every collection contract — a <c>SortOrder</c>, a <c>Cursor</c>, a feature's own
/// <c>TodoListPageRequest</c> — is a record built only by a validating factory. The value of that
/// convention is only real if nothing can construct one having skipped the factory, so these rules
/// check the constructor surface itself rather than trusting the convention was followed.
/// </para>
/// </summary>
public sealed class CollectionContractTests
{
    /// <summary><c>AppTemplate.Application.Common.Collections</c> or a feature's own <c>…Collections</c>.</summary>
    private const string _collectionsNamespacePattern = @"^AppTemplate\.Application\..*\.Collections$";

    private const string _portsNamespaceSuffix = ".Ports";

    #region 1. Every validated collection contract is unconstructible without validation

    [Fact]
    public void EveryRecordInACollectionsNamespace_HasNoPublicConstructor()
    {
        var candidates = ArchitectureAssemblies.Application
            .GetTypes()
            .Where(type => type is { IsClass: true, IsNested: false })
            .Where(type => !Attribute.IsDefined(type, typeof(CompilerGeneratedAttribute)))
            .Where(type => type.Namespace is not null && IsCollectionsNamespace(type.Namespace))
            .Where(TypeFacts.IsRecord)
            .ToList();

        candidates.ShouldNotBeEmpty(
            "No record type was found in a namespace matching " +
            $"'{_collectionsNamespacePattern}', so this rule is no longer describing the collection " +
            "contracts (SortableField, SortTerm, SortOrder, Cursor, PageRequest, SearchTerm, " +
            "TodoListFilter, TodoListPageRequest). Either the convention was renamed or the rule is stale.");

        // The floor comes from Common/Collections (SortableField, SortOrder, SortTerm, Cursor,
        // PageRequest, SearchTerm) plus TodoLists/Collections (TodoListFilter, TodoListPageRequest).
        candidates.Count.ShouldBeGreaterThanOrEqualTo(
            8,
            "Fewer collection-contract records were found than this template is known to declare. " +
            "The discovery in this rule has stopped matching them.");

        candidates
            .Where(HasAPublicConstructor)
            .Select(type => type.FullName ?? type.Name)
            .Order(StringComparer.Ordinal)
            .ShouldBeEmpty(
                "A collection contract with a public constructor can be built having skipped its " +
                "validating factory (SortOrder.Parse, Cursor.Decode, PageRequest.Create, …), and would " +
                "then reach the persistence layer unvalidated. Make the constructor private and expose " +
                "only the factory.");
    }

    /// <summary>
    /// The exclusion the rule above depends on, stated as its own assertion: a policy is a plain
    /// class, not a record, and is deliberately allowed a public constructor
    /// (<see cref="ArchitectureAssemblies.Application"/> discovers it by a parameterless one). This
    /// proves that exemption is explicit rather than accidental — the predicate above excludes it
    /// because <c>TypeFacts.IsRecord</c> says no, not because nobody looked.
    /// </summary>
    [Fact]
    public void APolicyClass_IsNotARecordAndIsExemptFromTheConstructorRule()
    {
        var policies = ArchitectureAssemblies.Application
            .GetTypes()
            .Where(type => type is { IsClass: true, IsNested: false })
            .Where(type => type.Namespace is not null && IsCollectionsNamespace(type.Namespace))
            .Where(type => typeof(ICollectionPolicy).IsAssignableFrom(type))
            .ToList();

        policies.ShouldNotBeEmpty(
            "No ICollectionPolicy implementation was found in a Collections namespace, so this rule " +
            "is not proving anything about the exemption it describes.");

        policies.ShouldAllBe(
            policy => !TypeFacts.IsRecord(policy),
            "A collection policy is a plain class with a public parameterless constructor, exempt " +
            "from the no-public-constructor rule above. One turning into a record would silently " +
            "start being covered by that rule instead of this exemption.");
    }

    #endregion

    #region 2. Every ICollectionPolicy is internally consistent

    [Fact]
    public void EveryCollectionPolicy_IsInternallyConsistent()
    {
        var policyTypes = ArchitectureAssemblies.Application
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, IsNested: false })
            .Where(type => typeof(ICollectionPolicy).IsAssignableFrom(type))
            .ToList();

        policyTypes.ShouldNotBeEmpty(
            "No ICollectionPolicy implementation was found in the Application assembly, so this rule " +
            "is guarding nothing. Either the interface was renamed or no feature has whitelisted a " +
            "collection endpoint any more.");

        var failures = new List<string>();

        foreach (var policyType in policyTypes)
        {
            var policy = (ICollectionPolicy)(Activator.CreateInstance(policyType)
                ?? throw new InvalidOperationException(
                    $"{policyType.FullName} could not be instantiated by its parameterless constructor."));

            failures.AddRange(ConsistencyFailuresOf(policyType, policy));
        }

        failures.Order(StringComparer.Ordinal).ShouldBeEmpty(
            "One or more ICollectionPolicy implementations are internally inconsistent.");
    }

    private static IEnumerable<string> ConsistencyFailuresOf(Type policyType, ICollectionPolicy policy)
    {
        string name = policyType.Name;

        if (policy.SortableFields.Count == 0)
        {
            yield return $"{name}: SortableFields is empty.";
            yield break;
        }

        if (policy.SortableFields.Any(field => string.IsNullOrWhiteSpace(field.Name)))
        {
            yield return $"{name}: a SortableField has a null or blank Name.";
        }

        var duplicates = policy.SortableFields
            .GroupBy(field => field.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            yield return $"{name}: SortableFields names the same field more than once " +
                $"(case-insensitively): {string.Join(", ", duplicates)}.";
        }

        if (policy.MaxSortTerms < 1)
        {
            yield return $"{name}: MaxSortTerms is {policy.MaxSortTerms}, expected at least 1.";
        }

        if (policy.MaxPageSize < 1)
        {
            yield return $"{name}: MaxPageSize is {policy.MaxPageSize}, expected at least 1.";
        }

        if (policy.DefaultPageSize < 1 || policy.DefaultPageSize > policy.MaxPageSize)
        {
            yield return $"{name}: DefaultPageSize is {policy.DefaultPageSize}, expected between 1 " +
                $"and MaxPageSize ({policy.MaxPageSize}).";
        }

        // The important one: a feature's own default is parsed by exactly the code path a caller's
        // 'sort' input takes, so a typo in DefaultSort fails here rather than shipping.
        var parsed = SortOrder.Parse(policy.DefaultSort, policy);

        if (parsed.IsFailure)
        {
            yield return $"{name}: DefaultSort ('{policy.DefaultSort}') does not parse against its " +
                $"own SortableFields: {parsed.Error!.Message}";

            yield break;
        }

        if (parsed.Value.Terms.Count > policy.MaxSortTerms)
        {
            yield return $"{name}: DefaultSort ('{policy.DefaultSort}') carries " +
                $"{parsed.Value.Terms.Count} term(s), more than its own MaxSortTerms " +
                $"({policy.MaxSortTerms}).";
        }
    }

    #endregion

    #region 3. No collection contract reaches a port unvalidated

    [Fact]
    public void NoPortParameter_FromACollectionsNamespace_HasAPublicConstructor()
    {
        var portInterfaces = ArchitectureAssemblies.Application
            .GetTypes()
            .Where(type => type is { IsInterface: true, IsNested: false })
            .Where(type => type.Namespace is not null
                && type.Namespace.EndsWith(_portsNamespaceSuffix, StringComparison.Ordinal))
            .ToList();

        portInterfaces.ShouldNotBeEmpty(
            $"No interface was found in a namespace ending '{_portsNamespaceSuffix}', so this rule is " +
            "no longer describing the application layer's ports.");

        var collectionParameterTypes = portInterfaces
            .SelectMany(port => port.GetMethods())
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .Where(parameterType => parameterType.Namespace is not null
                && IsCollectionsNamespace(parameterType.Namespace))
            .Distinct()
            .ToList();

        collectionParameterTypes.ShouldNotBeEmpty(
            "No port method takes a parameter from a *.Collections namespace (expected at least " +
            "ITodoListQueries.GetForOwnerAsync's TodoListPageRequest), so the rule below cannot go " +
            "stale silently when a port is renamed or a parameter's type changes.");

        collectionParameterTypes
            .Where(HasAPublicConstructor)
            .Select(type => type.FullName ?? type.Name)
            .Order(StringComparer.Ordinal)
            .ShouldBeEmpty(
                "A port parameter drawn from a *.Collections namespace must be unconstructible without " +
                "validation, exactly like the contracts in rule 1 above — otherwise a caller could " +
                "hand the read side a request that skipped SortOrder.Parse / Cursor.Decode / " +
                "PageRequest.Create.");
    }

    #endregion

    private static bool IsCollectionsNamespace(string @namespace) =>
        Regex.IsMatch(@namespace, _collectionsNamespacePattern, RegexOptions.None, TimeSpan.FromSeconds(5));

    private static bool HasAPublicConstructor(Type type) =>
        type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length > 0;
}

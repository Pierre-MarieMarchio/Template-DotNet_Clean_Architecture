using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using AppTemplate.Application.Common;
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
            $"'{_collectionsNamespacePattern}', so this rule is no longer describing the shared " +
            "collection contracts (SortableField, SortTerm, SortOrder, Cursor, PageRequest, " +
            "SearchTerm). Either the convention was renamed or the rule is stale.");

        // SortableField, SortOrder, SortTerm, Cursor, PageRequest, SearchTerm. A feature's own
        // paging contracts are not counted here: they travel with the port that accepts them, and
        // NoPortParameter_IsPartlyValidated is what holds them to the same standard.
        candidates.Count.ShouldBeGreaterThanOrEqualTo(
            6,
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
            .Where(type => typeof(ICollectionPolicy).IsAssignableFrom(type))
            .ToList();

        policies.ShouldNotBeEmpty(
            "No ICollectionPolicy implementation was found, so this rule is not proving anything " +
            "about the exemption it describes.");

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

    #region 3. Nothing that validates itself can be built having skipped the validation

    /// <summary>
    /// A record that offers a factory returning <c>Result&lt;itself&gt;</c> has declared that it can be
    /// asked for and refused — so a public constructor beside that factory is a second way in that
    /// answers nothing, and the value the read side receives may never have been checked.
    /// <para>
    /// The discriminant is the type's own shape rather than the folder it sits in, which is the point:
    /// a feature's paging contract travels with the port that accepts it and a shared one lives in
    /// <c>Common/Collections</c>, so any rule keyed on location stops describing them the first time
    /// something moves. This one cannot be defeated by moving a file.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryRecord_WithAValidatingFactory_HasNoPublicConstructor()
    {
        var validated = ArchitectureAssemblies.Application
            .GetTypes()
            .Where(type => type is { IsClass: true, IsNested: false })
            .Where(type => !Attribute.IsDefined(type, typeof(CompilerGeneratedAttribute)))
            .Where(TypeFacts.IsRecord)
            .Where(HasAValidatingFactory)
            .ToList();

        // Cursor, PageRequest, SearchTerm, SortOrder from Common/Collections, the feature's own
        // TodoListFilter and TodoListPageRequest, wherever the layout puts them.
        validated.Count.ShouldBeGreaterThanOrEqualTo(
            6,
            "Fewer self-validating records were found than this template is known to declare. The " +
            "discovery in this rule has stopped matching them, most likely because a factory stopped " +
            "returning Result<T>.");

        validated
            .Where(HasAPublicConstructor)
            .Select(type => type.FullName ?? type.Name)
            .Order(StringComparer.Ordinal)
            .ShouldBeEmpty(
                "A record offering a Result-returning factory must not also offer a public " +
                "constructor: the factory is where the value is refused, and a caller reaching past " +
                "it hands the rest of the system a contract nobody checked.");
    }

    /// <summary>
    /// Proves the discovery above can select, by applying it to a record written here to have exactly
    /// the shape it looks for. If this failed, the rule would be filtering everything out and passing
    /// over an empty set.
    /// </summary>
    [Fact]
    public void TheValidatingFactoryRule_IsSensitive_AndSelectsSuchARecord()
    {
        HasAValidatingFactory(typeof(DeliberatelyValidatedRecord)).ShouldBeTrue(
            $"{nameof(DeliberatelyValidatedRecord)} declares a static factory returning " +
            "Result<itself>, which is precisely what the discovery looks for.");

        HasAPublicConstructor(typeof(DeliberatelyValidatedRecord)).ShouldBeTrue(
            "The fixture is written with a public constructor, so a rule that selected it would " +
            "report it. That is what makes the emptiness of the real result meaningful.");
    }

    /// <summary>
    /// A static method returning <see cref="Result{TValue}"/> of the very type that declares it.
    /// Non-public factories count: <c>SearchTerm.Create</c> and <c>Cursor.Decode</c> are internal,
    /// and are no less the only way in.
    /// </summary>
    private static bool HasAValidatingFactory(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Any(method => method.ReturnType.IsGenericType
                && method.ReturnType.GetGenericTypeDefinition() == typeof(Result<>)
                && method.ReturnType.GetGenericArguments()[0] == type);

    #endregion

    private static bool IsCollectionsNamespace(string @namespace) =>
        Regex.IsMatch(@namespace, _collectionsNamespacePattern, RegexOptions.None, TimeSpan.FromSeconds(5));

    private static bool HasAPublicConstructor(Type type) =>
        type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length > 0;
}

/// <summary>
/// A record with both a Result-returning factory and a public constructor — the shape
/// <see cref="CollectionContractTests.EveryRecord_WithAValidatingFactory_HasNoPublicConstructor"/>
/// forbids. It lives in the test project so the sensitivity proof needs no violation in the product
/// code, and so the rule itself — which reads the application assembly — never sees it.
/// </summary>
internal sealed record DeliberatelyValidatedRecord(string Value)
{
    public static Result<DeliberatelyValidatedRecord> Create(string value) =>
        Result.Success(new DeliberatelyValidatedRecord(value));
}

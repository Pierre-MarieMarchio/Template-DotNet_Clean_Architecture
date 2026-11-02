using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Policies;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class CollectionContractTests
{
    private const string _collectionsNamespacePattern = @"^AppTemplate\.Application\..*\.Collections$";

    private static IEnumerable<Type> ApplicationLayerTypes =>
        ArchitectureAssemblies.ApplicationLayer.SelectMany(assembly => assembly.GetTypes());

    [Fact]
    public void EveryRecordInACollectionsNamespace_HasNoPublicConstructor()
    {
        var candidates = ApplicationLayerTypes
            .Where(type => type is { IsClass: true, IsNested: false })
            .Where(type => !Attribute.IsDefined(type, typeof(CompilerGeneratedAttribute)))
            .Where(type => type.Namespace is not null && IsCollectionsNamespace(type.Namespace))
            .Where(TypeFacts.IsRecord)
            .ToList();

        candidates.ShouldNotBeEmpty(
            "No record type was found in a namespace matching " +
            $"'{_collectionsNamespacePattern}', so this rule is no longer describing the shared " +
            "collection contracts (SortableField, SortTerm, SortOrder, CollectionOrder, Cursor, " +
            "PageRequest, SearchTerm, FeaturePageRequest). Either the convention was renamed or the " +
            "rule is stale.");

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

    [Fact]
    public void APolicyClass_IsNotARecordAndIsExemptFromTheConstructorRule()
    {
        var policies = ApplicationLayerTypes
            .Where(type => type is { IsClass: true, IsNested: false })
            .Where(type => typeof(ICollectionPolicy).IsAssignableFrom(type))
            .ToList();

        //#if (anyFeature)
        policies.ShouldNotBeEmpty(
            "No ICollectionPolicy implementation was found, so this rule is not proving anything " +
            "about the exemption it describes.");

        //#endif
        policies.ShouldAllBe(
            policy => !TypeFacts.IsRecord(policy),
            "A collection policy is a plain class with a public parameterless constructor, exempt " +
            "from the no-public-constructor rule above. One turning into a record would silently " +
            "start being covered by that rule instead of this exemption.");
    }

    [Fact]
    public void EveryCollectionPolicy_IsInternallyConsistent()
    {
        var policyTypes = ApplicationLayerTypes
            .Where(type => type is { IsClass: true, IsAbstract: false, IsNested: false })
            .Where(type => typeof(ICollectionPolicy).IsAssignableFrom(type))
            .ToList();

        //#if (anyFeature)
        policyTypes.ShouldNotBeEmpty(
            "No ICollectionPolicy implementation was found in the Application assembly, so this rule " +
            "is guarding nothing. Either the interface was renamed or no feature has whitelisted a " +
            "collection endpoint any more.");

        //#endif
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

    [Fact]
    public void EveryRecord_WithAValidatingFactory_HasNoPublicConstructor()
    {
        var validated = ApplicationLayerTypes
            .Where(type => type is { IsClass: true, IsNested: false })
            .Where(type => !Attribute.IsDefined(type, typeof(CompilerGeneratedAttribute)))
            .Where(TypeFacts.IsRecord)
            .Where(HasAValidatingFactory)
            .ToList();

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

    private static bool HasAValidatingFactory(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Any(method => method.ReturnType.IsGenericType
                && method.ReturnType.GetGenericTypeDefinition() == typeof(Result<>)
                && method.ReturnType.GetGenericArguments()[0] == type);

    private static bool IsCollectionsNamespace(string @namespace) =>
        Regex.IsMatch(@namespace, _collectionsNamespacePattern, RegexOptions.None, TimeSpan.FromSeconds(5));

    private static bool HasAPublicConstructor(Type type) =>
        type.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length > 0;
}

internal sealed record DeliberatelyValidatedRecord(string Value)
{
    public static Result<DeliberatelyValidatedRecord> Create(string value) =>
        Result.Success(new DeliberatelyValidatedRecord(value));
}

using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class UseCaseConventionTests
{
    private const string _useCaseSuffix = "UseCase";

    private const string _useCaseNamespaceSuffixPattern =
        @"\.Features\.[^.]+\.UseCases(\.[^.]+)*$";

    private const string _useCaseFolderNamespaceSuffixPattern =
        @"\.Features\.[^.]+\.UseCases\.(Commands|Queries)\.[^.]+$";

    private static readonly string[] _inputContractSuffixes = ["Command", "Query", "Request"];

    [Fact]
    public void UseCases_LiveUnderTheirVerticalsUseCasesFolder()
    {
        var useCases = Types.InAssemblies(ArchitectureAssemblies.ApplicationLayer)
            .That()
            .AreClasses()
            .And()
            .HaveNameEndingWith(_useCaseSuffix);

        var matched = RuleAssertions.RequireTypes(useCases, "a class whose name ends with 'UseCase'");

        SourceDeclarations.WalkComplaints.ShouldBeEmpty(
            "The source-tree walk this rule measures its candidate set against did not read the " +
            $"tree it describes.{Environment.NewLine}  " +
            string.Join($"{Environment.NewLine}  ", SourceDeclarations.WalkComplaints));

        var divergence = SourceDeclarations.Divergence(
            SourceDeclarations.UseCaseFullNames,
            matched.Select(SourceDeclarations.WithoutArity),
            "matched by this rule's suffix filter");

        divergence.ShouldBeEmpty(
            "The candidate set is measured against the declarations on disk rather than floored at a " +
            "number, because a floor cannot be wrong — only stale, and a stale one lets this rule " +
            "check a fraction of the layer while reporting that the convention holds. " +
            $"({SourceDeclarations.UseCaseFullNames.Count} declared across the application layer, " +
            $"{matched.Count} matched here.){Environment.NewLine}  " +
            string.Join($"{Environment.NewLine}  ", divergence));

        matched
            .Where(useCase => !IsUnderAUseCasesFolder(useCase))
            .Select(useCase => useCase.FullName ?? useCase.Name)
            .Order(StringComparer.Ordinal)
            .ShouldBeEmpty(
                "A use case belongs in Features/<Vertical>/UseCases/ of the project that declares " +
                "it. The folder layout is the only index of what the application can do, because " +
                "registration is explicit rather than scanned. Expected namespace shape: " +
                $"<Project>{_useCaseNamespaceSuffixPattern}");
    }

    private static string RootOf(Type type) =>
        type.Assembly.GetName().Name
        ?? throw new InvalidOperationException(
            $"'{type.FullName}' is declared in an assembly with no simple name.");

    private static bool MatchesInItsOwnProject(Type type, string suffixPattern) =>
        type.Namespace is { } declared
        && Regex.IsMatch(
            declared,
            $"^{Regex.Escape(RootOf(type))}{suffixPattern}",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

    private static bool IsUnderAUseCasesFolder(Type type) =>
        MatchesInItsOwnProject(type, _useCaseNamespaceSuffixPattern);

    private static bool IsInAUseCaseFolder(Type type) =>
        MatchesInItsOwnProject(type, _useCaseFolderNamespaceSuffixPattern);

    [Fact]
    public void UseCases_AreSealed()
    {
        var useCases = Types.InAssemblies(ArchitectureAssemblies.ApplicationLayer)
            .That()
            .AreClasses()
            .And()
            .HaveNameEndingWith(_useCaseSuffix);

        RuleAssertions.RequireTypes(useCases, "a class whose name ends with 'UseCase'");

        useCases.Should()
            .BeSealed()
            .GetResult()
            .ShouldHold(
                "A use case is one operation with one implementation. Leaving it open invites a " +
                "subclass, and a caller that depends on the subclass instead of the operation.");
    }

    [Fact]
    public void UseCases_ArePublic()
    {
        var useCases = Types.InAssemblies(ArchitectureAssemblies.ApplicationLayer)
            .That()
            .AreClasses()
            .And()
            .HaveNameEndingWith(_useCaseSuffix);

        RuleAssertions.RequireTypes(useCases, "a class whose name ends with 'UseCase'");

        useCases.Should()
            .BePublic()
            .GetResult()
            .ShouldHold(
                "The API resolves use cases from the container by concrete type, so a use case that " +
                "is not public cannot be reached by a controller.");
    }

    [Fact]
    public void EveryUseCaseFolder_HoldsOneUseCase_AndIsNamedForIt()
    {
        var folders = ArchitectureAssemblies.ApplicationLayer
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsNested: false })
            .Where(type => !Attribute.IsDefined(type, typeof(CompilerGeneratedAttribute)))
            .Where(type => IsInAUseCaseFolder(type))
            .GroupBy(type => type.Namespace!, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToList();

        SourceDeclarations.WalkComplaints.ShouldBeEmpty(
            "The source-tree walk this rule measures its folder set against did not read the tree " +
            $"it describes.{Environment.NewLine}  " +
            string.Join($"{Environment.NewLine}  ", SourceDeclarations.WalkComplaints));

        var unread = SourceDeclarations.Divergence(
            SourceDeclarations.UseCaseOperationNamespaces,
            folders.Select(folder => folder.Key),
            "read as an operation folder by this rule");

        unread.ShouldBeEmpty(
            "The set of operation folders is measured off disk rather than floored at a number, so " +
            "adding a vertical needs no edit here and a folder this rule has stopped reading cannot " +
            $"pass as one it inspected. ({SourceDeclarations.UseCaseOperationNamespaces.Count} " +
            $"folders hold a use case on disk, {folders.Count} were read here.)" +
            $"{Environment.NewLine}  " + string.Join($"{Environment.NewLine}  ", unread));

        var failures = new List<string>();

        foreach (var folder in folders)
        {
            string operation = folder.Key[(folder.Key.LastIndexOf('.') + 1)..];

            var useCases = folder
                .Where(type => type is { IsClass: true, IsAbstract: false })
                .Where(type => type.Name.EndsWith(_useCaseSuffix, StringComparison.Ordinal))
                .ToList();

            if (useCases.Count != 1)
            {
                failures.Add(
                    $"{folder.Key} holds {useCases.Count} use cases " +
                    $"({string.Join(", ", useCases.Select(type => type.Name).Order(StringComparer.Ordinal))}), " +
                    "expected exactly one.");

                continue;
            }

            if (!string.Equals(useCases[0].Name, operation + _useCaseSuffix, StringComparison.Ordinal))
            {
                failures.Add(
                    $"{folder.Key} is named '{operation}' but holds '{useCases[0].Name}'. A folder " +
                    $"named for an operation must hold '{operation}{_useCaseSuffix}'.");
            }

            if (!folder.Any(type => type.IsInterface
                && string.Equals(type.Name, "I" + operation + _useCaseSuffix, StringComparison.Ordinal)))
            {
                failures.Add(
                    $"{folder.Key} declares no 'I{operation}{_useCaseSuffix}'. Callers resolve a use " +
                    "case by its named interface, so one that has none cannot be reached.");
            }

            failures.AddRange(folder
                .Where(TypeFacts.IsRecord)
                .Where(type => _inputContractSuffixes.Any(
                    suffix => type.Name.EndsWith(suffix, StringComparison.Ordinal)))
                .Where(type => !_inputContractSuffixes.Any(
                    suffix => string.Equals(type.Name, operation + suffix, StringComparison.Ordinal)))
                .Select(type =>
                    $"{folder.Key} holds input contract '{type.Name}', which names an operation other " +
                    $"than '{operation}'. The input a use case accepts is named for that use case."));
        }

        failures.Order(StringComparer.Ordinal).ShouldBeEmpty(
            "The folder layout is the only index of what the application can do, because registration " +
            "is explicit rather than scanned. A folder that does not say what is in it, or holds more " +
            "than one operation, breaks that index.");
    }
}

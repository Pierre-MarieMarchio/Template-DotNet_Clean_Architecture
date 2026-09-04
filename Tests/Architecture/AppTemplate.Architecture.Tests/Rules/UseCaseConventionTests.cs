using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

/// <summary>
/// Where a use case lives and what shape it has.
/// <para>
/// The convention is load-bearing rather than cosmetic: the container registers use cases by
/// concrete type, one explicit line each, and a reader's only index of what exists is the folder
/// layout. A use case in the wrong place is one nobody finds; a use case that is not sealed
/// invites a subclass that a caller then depends on instead of the real thing.
/// </para>
/// </summary>
public sealed class UseCaseConventionTests
{
    private const string _useCaseSuffix = "UseCase";

    /// <summary>
    /// <c>AppTemplate.Application.Features.&lt;Vertical&gt;.UseCases[.Commands|.Queries|…]</c>.
    /// </summary>
    private const string _useCaseNamespacePattern =
        @"^AppTemplate\.Application\.Features\.[^.]+\.UseCases(\.[^.]+)*$";

    /// <summary>
    /// One operation's own folder:
    /// <c>AppTemplate.Application.Features.&lt;Vertical&gt;.UseCases.Commands|Queries.&lt;Operation&gt;</c>.
    /// </summary>
    private const string _useCaseFolderNamespacePattern =
        @"^AppTemplate\.Application\.Features\.[^.]+\.UseCases\.(Commands|Queries)\.[^.]+$";

    /// <summary>
    /// What a use case's input is called. The record is declared in the same file as the operation
    /// that consumes it, which is the only other thing a UseCases folder may hold.
    /// </summary>
    private static readonly string[] _inputContractSuffixes = ["Command", "Query", "Request"];

    [Fact]
    public void UseCases_LiveUnderTheirVerticalsUseCasesFolder()
    {
        var useCases = Types.InAssembly(ArchitectureAssemblies.Application)
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
            $"({SourceDeclarations.UseCaseFullNames.Count} declared in the application project, " +
            $"{matched.Count} matched here.){Environment.NewLine}  " +
            string.Join($"{Environment.NewLine}  ", divergence));

        useCases.Should()
            .ResideInNamespaceMatching(_useCaseNamespacePattern)
            .GetResult()
            .ShouldHold(
                "A use case belongs in Features/<Vertical>/UseCases/. The folder layout is the only " +
                "index of what the application can do, because registration is explicit rather " +
                $"than scanned. Expected namespace pattern: {_useCaseNamespacePattern}");
    }

    [Fact]
    public void UseCases_AreSealed()
    {
        var useCases = Types.InAssembly(ArchitectureAssemblies.Application)
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
        var useCases = Types.InAssembly(ArchitectureAssemblies.Application)
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

    /// <summary>
    /// The converse rule, and the one that actually catches a rename. Each use case owns a folder
    /// holding the operation, the interface a caller resolves it by, the input it accepts and
    /// whatever else serves that one operation — so "everything here is a use case or its input" is
    /// not the shape to assert. What holds instead, and holds harder: the folder is named for
    /// the operation, and it contains exactly one.
    /// <para>
    /// That is what makes the layout an index. A second use case sharing a folder is one a reader
    /// looking for it by name will not find; a folder whose name has drifted from the operation
    /// inside it sends that reader somewhere else entirely. Both are invisible to every naming rule
    /// above, which only ever looks at a type in isolation.
    /// </para>
    /// <para>
    /// Written with reflection rather than NetArchTest because the namespace of a compiler-generated
    /// type — an async state machine, a lambda closure — is the namespace of the method that produced
    /// it, and those are not named after any convention. Reflection lets them be excluded explicitly
    /// instead of trusting the rule engine to have filtered them.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryUseCaseFolder_HoldsOneUseCase_AndIsNamedForIt()
    {
        var folders = ArchitectureAssemblies.Application
            .GetTypes()
            .Where(type => type is { IsNested: false })
            .Where(type => !Attribute.IsDefined(type, typeof(CompilerGeneratedAttribute)))
            .Where(type => type.Namespace is not null
                && Regex.IsMatch(type.Namespace, _useCaseFolderNamespacePattern, RegexOptions.None, TimeSpan.FromSeconds(5)))
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

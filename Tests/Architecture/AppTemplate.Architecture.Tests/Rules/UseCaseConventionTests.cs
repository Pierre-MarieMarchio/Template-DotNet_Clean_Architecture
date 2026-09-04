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
        matched.Count.ShouldBeGreaterThanOrEqualTo(
            14,
            "The application layer has eight TodoList use cases and six Auth use cases. Finding " +
            "fewer means the discovery in this rule has stopped matching them.");

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
    /// The converse rule, and the one that actually catches a rename: something sitting in a
    /// <c>UseCases</c> folder that is no longer named <c>…UseCase</c> escapes every rule above.
    /// <para>
    /// A use case file holds two things — the operation and its input contract, declared beside it as
    /// a record (<c>CreateTodoListCommand</c> next to <c>CreateTodoListUseCase</c>). Both are allowed
    /// here; nothing else is. A service, a helper or a mapper in this folder would be invisible to
    /// every naming rule above.
    /// </para>
    /// <para>
    /// Written with reflection rather than NetArchTest because the namespace of a compiler-generated
    /// type — an async state machine, a lambda closure — is the namespace of the method that produced
    /// it, and those are not named after any convention. Reflection lets them be excluded explicitly
    /// instead of trusting the rule engine to have filtered them.
    /// </para>
    /// </summary>
    [Fact]
    public void EverythingInAUseCasesFolder_IsAUseCaseOrItsInputContract()
    {
        var inUseCaseNamespaces = ArchitectureAssemblies.Application
            .GetTypes()
            .Where(type => type is { IsClass: true, IsNested: false })
            .Where(type => !Attribute.IsDefined(type, typeof(CompilerGeneratedAttribute)))
            .Where(type => type.Namespace is not null
                && Regex.IsMatch(type.Namespace, _useCaseNamespacePattern, RegexOptions.None, TimeSpan.FromSeconds(5)))
            .ToList();

        inUseCaseNamespaces.ShouldNotBeEmpty(
            "No types were found in a Features/<Vertical>/UseCases namespace, so this rule is no " +
            "longer describing the application layer.");

        var useCases = inUseCaseNamespaces
            .Where(type => !TypeFacts.IsRecord(type))
            .ToList();

        var inputContracts = inUseCaseNamespaces
            .Where(TypeFacts.IsRecord)
            .ToList();

        useCases.ShouldNotBeEmpty("No use case classes were found alongside their input contracts.");
        inputContracts.ShouldNotBeEmpty(
            "No input-contract records were found in the UseCases folders, so the second half of " +
            "this rule is guarding nothing.");

        useCases
            .Where(type => !type.Name.EndsWith(_useCaseSuffix, StringComparison.Ordinal))
            .Select(type => type.FullName ?? type.Name)
            .Order(StringComparer.Ordinal)
            .ShouldBeEmpty(
                "A class in a UseCases folder is a use case and is named for it. Anything else here " +
                "is covered by none of the rules above, and a use case renamed away from the suffix " +
                "stops being covered by any of them.");

        inputContracts
            .Where(type => !_inputContractSuffixes.Any(
                suffix => type.Name.EndsWith(suffix, StringComparison.Ordinal)))
            .Select(type => type.FullName ?? type.Name)
            .Order(StringComparer.Ordinal)
            .ShouldBeEmpty(
                "A record in a UseCases folder is the input a use case takes, named for what it is: " +
                $"{string.Join(" or ", _inputContractSuffixes)}. A DTO belongs in the vertical's Dtos " +
                "folder, where the rest of its shape lives.");
    }
}

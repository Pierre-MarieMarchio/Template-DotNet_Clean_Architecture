using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class UseCaseConventionTests
{
    private const string _useCaseFolderNamespaceSuffixPattern =
        @"\.Features\.[^.]+\.UseCases\.(Commands|Queries)\.[^.]+$";

    private static readonly string[] _inputContractSuffixes = ["Command", "Query", "Request"];

    [Fact]
    public void UseCases_LiveUnderTheirVerticalsUseCasesFolder()
    {
        var matched = UseCaseTypes.InTheApplicationLayer;

        matched.ShouldNotBeEmpty("No use case was found in the application layer.");

        SourceDeclarations.WalkComplaints.ShouldBeEmpty(
            "The source-tree walk this rule measures its candidate set against did not read the " +
            $"tree it describes.{Environment.NewLine}  " +
            string.Join($"{Environment.NewLine}  ", SourceDeclarations.WalkComplaints));

        var divergence = SourceDeclarations.Divergence(
            SourceDeclarations.UseCaseFullNames,
            matched.Select(SourceDeclarations.WithoutArity),
            "found as a handler of a message by this rule");

        divergence.ShouldBeEmpty(
            "The candidate set is measured against the declarations on disk rather than floored at a " +
            "number, because a floor cannot be wrong — only stale, and a stale one lets this rule " +
            "check a fraction of the layer while reporting that the convention holds. " +
            $"({SourceDeclarations.UseCaseFullNames.Count} declared across the application layer, " +
            $"{matched.Count} matched here.){Environment.NewLine}  " +
            string.Join($"{Environment.NewLine}  ", divergence));

        matched
            .Where(useCase => !IsInAUseCaseFolder(useCase))
            .Select(useCase => useCase.FullName ?? useCase.Name)
            .Order(StringComparer.Ordinal)
            .ShouldBeEmpty(
                "A use case belongs in Features/<Vertical>/UseCases/<Commands|Queries>/<UseCase>/ of " +
                "the project that declares it. The folder layout is the only index of what the " +
                "application can do. Move each type named here, and its namespace with it, to that " +
                $"folder: its namespace is <Project>{_useCaseFolderNamespaceSuffixPattern}");
    }

    [Fact]
    public void UseCases_AreSealedAndPublic()
    {
        var useCases = UseCaseTypes.InTheApplicationLayer;

        useCases.ShouldNotBeEmpty("No use case was found in the application layer.");

        NotSealedAndPublic(useCases).ShouldBeEmpty(
            "A use case is one operation with one implementation, so it is sealed: an open one " +
            "invites a subclass, and a caller that depends on the subclass instead of the " +
            "operation. It is public, as its message is, because a host's generated mediator " +
            "names it. Declare each type named here as 'public sealed class'.");
    }

    private static List<string> NotSealedAndPublic(IEnumerable<Type> useCases) =>
        [.. useCases
            .Where(useCase => !useCase.IsSealed || !useCase.IsPublic)
            .Select(useCase => useCase.FullName ?? useCase.Name)
            .Order(StringComparer.Ordinal)];

    private static string RootOf(Type type) =>
        type.Assembly.GetName().Name
        ?? throw new InvalidOperationException(
            $"'{type.FullName}' is declared in an assembly with no simple name.");

    private static bool MatchesInItsOwnProject(string root, string? declared, string suffixPattern) =>
        declared is not null
        && Regex.IsMatch(
            declared,
            $"^{Regex.Escape(root)}{suffixPattern}",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

    private static bool IsInAUseCaseFolder(Type type) =>
        IsInAUseCaseFolder(RootOf(type), type.Namespace);

    private static bool IsInAUseCaseFolder(string root, string? @namespace) =>
        MatchesInItsOwnProject(root, @namespace, _useCaseFolderNamespaceSuffixPattern);

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
            string nature = folder.Key.Contains(".UseCases.Commands.", StringComparison.Ordinal) ? "Command" : "Query";

            var useCases = folder.Where(UseCaseTypes.IsUseCase).ToList();

            if (useCases.Count != 1)
            {
                failures.Add(
                    $"{folder.Key} holds {useCases.Count} use cases " +
                    $"({string.Join(", ", useCases.Select(type => type.Name).Order(StringComparer.Ordinal))}), " +
                    "expected exactly one: keep one use case here and move each other to a folder " +
                    "of its own, named for it.");

                continue;
            }

            failures.AddRange(HandlerFailures(
                folder.Key,
                operation,
                nature,
                useCases[0],
                UseCaseTypes.ContractOf(useCases[0]).GetGenericArguments()[0]));

            failures.AddRange(MisnamedInputContracts(folder.Key, operation, folder));
        }

        failures.Order(StringComparer.Ordinal).ShouldBeEmpty(
            "The folder layout is the only index of what the application can do. A folder that does " +
            "not say what is in it, or holds more than one operation, breaks that index: give each " +
            "operation its own folder under UseCases/<Commands|Queries>/, named for it, holding its " +
            "message and its handler, as each line above says.");
    }

    private static IEnumerable<string> MisnamedInputContracts(string folder, string operation, IEnumerable<Type> types) =>
        types
            .Where(TypeFacts.IsRecord)
            .Where(type => _inputContractSuffixes.Any(
                suffix => type.Name.EndsWith(suffix, StringComparison.Ordinal)))
            .Where(type => !_inputContractSuffixes.Any(
                suffix => string.Equals(type.Name, operation + suffix, StringComparison.Ordinal)))
            .Select(type =>
                $"{folder} holds input contract '{type.Name}', which names an operation other " +
                $"than '{operation}'. The input a use case accepts is named for that use case: " +
                $"rename it '{operation}{SuffixOf(type)}', or move it to the folder of the operation it names.");

    private static string SuffixOf(Type type) =>
        _inputContractSuffixes.First(suffix => type.Name.EndsWith(suffix, StringComparison.Ordinal));

    private static IEnumerable<string> HandlerFailures(
        string folder,
        string operation,
        string nature,
        Type handler,
        Type handled)
    {
        string message = operation + nature;

        if (!string.Equals(handler.Name, message + "Handler", StringComparison.Ordinal))
        {
            yield return
                $"{folder} is named '{operation}' and sits under {nature} use cases, but holds " +
                $"'{handler.Name}'. Rename the handler '{message}Handler', or move it to the folder of the operation it handles.";
        }

        if (!string.Equals(handled.Name, message, StringComparison.Ordinal)
            || !string.Equals(handled.Namespace, folder, StringComparison.Ordinal))
        {
            yield return
                $"{folder} holds a handler of '{handled.FullName}'. A use case's handler handles the " +
                $"message '{message}' declared beside it: declare that record in {folder} and handle it.";
        }

        if (!TypeFacts.IsRecord(handled) || !handled.IsSealed || !handled.IsPublic)
        {
            yield return
                $"{folder}: '{handled.Name}' is a message, and a message is a public sealed record: " +
                $"declare it 'public sealed record {handled.Name}(...)'.";
        }
    }

    [Fact]
    public void TheFolderRule_IsSensitive_AndTellsAUseCaseFolderFromAnyOtherNamespace()
    {
        IsInAUseCaseFolder("Shop", "Shop.Features.Parcels.UseCases.Commands.Place").ShouldBeTrue();
        IsInAUseCaseFolder("Shop", "Shop.Features.Parcels.UseCases.Queries.List").ShouldBeTrue();
        IsInAUseCaseFolder("Shop", "Shop.Features.Parcels.Commands.Place").ShouldBeFalse();
        IsInAUseCaseFolder("Shop", "Shop.Features.Parcels.UseCases.Commands.Place.Extra").ShouldBeFalse();
        IsInAUseCaseFolder("Shop", "Billing.Features.Parcels.UseCases.Commands.Place").ShouldBeFalse();
        IsInAUseCaseFolder("Shop", null).ShouldBeFalse();
    }

    [Fact]
    public void TheSealedRule_IsSensitive_AndSeesAnOpenAndANonPublicUseCase()
    {
        NotSealedAndPublic([typeof(string), typeof(Exception), typeof(HiddenUseCase)])
            .ShouldBe(
                [typeof(Exception).FullName!, typeof(HiddenUseCase).FullName!],
                ignoreOrder: true);
    }

    [Fact]
    public void TheInputContractRule_IsSensitive_AndSeesAContractNamedForAnotherOperation()
    {
        var failures = MisnamedInputContracts(
                "Shop.Folder",
                "Place",
                [typeof(PlaceCommand), typeof(CancelCommand), typeof(PlaceRequest), typeof(PlaceSummary)])
            .ToList();

        failures.ShouldHaveSingleItem().ShouldContain("rename it 'PlaceCommand'");
    }

    [Fact]
    public void TheHandlerRule_IsSensitive_AndSeesAMisnamedHandlerAnotherMessageAndAnImproperMessage()
    {
        string folder = typeof(UseCaseConventionTests).Namespace!;

        HandlerFailures(folder, "Shape", "Command", typeof(ShapeCommandHandler), typeof(ShapeCommand))
            .ShouldBeEmpty();

        HandlerFailures(folder, "Shape", "Command", typeof(ShapeHandlerNamedWrong), typeof(ShapeCommand))
            .ShouldHaveSingleItem()
            .ShouldContain("ShapeCommandHandler");

        HandlerFailures(folder, "Shape", "Command", typeof(ShapeCommandHandler), typeof(PlaceCommand))
            .ShouldNotBeEmpty();

        HandlerFailures(folder, "Place", "Command", typeof(PlaceCommandHandler), typeof(PlaceCommand))
            .ShouldHaveSingleItem()
            .ShouldContain("public sealed record");
    }

    private sealed class HiddenUseCase;

    private sealed record PlaceCommand;

    private sealed record CancelCommand;

    private sealed record PlaceRequest;

    private sealed record PlaceSummary;

    private sealed class PlaceCommandHandler;

    private sealed class ShapeCommandHandler;

    private sealed class ShapeHandlerNamedWrong;
}

public sealed record ShapeCommand;

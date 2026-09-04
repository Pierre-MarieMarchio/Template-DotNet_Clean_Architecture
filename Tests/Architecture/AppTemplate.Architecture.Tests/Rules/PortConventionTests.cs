using System.Reflection;
using System.Text.RegularExpressions;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

/// <summary>
/// What an application port is allowed to be.
/// <para>
/// A port is one capability an infrastructure module can satisfy, and the shape it must not take is a
/// façade: one interface covering everything a vertical needs, implemented by one class that then
/// owns the sequencing. When that happens the layer that is supposed to hold the decisions holds only
/// validation, an adapter decides what a failure means and when a token is issued, and there is
/// nothing left in the application layer to unit-test.
/// </para>
/// <para>
/// Every rule here establishes its candidate set before asserting the condition, because a condition
/// over an empty set is not a guarantee.
/// </para>
/// </summary>
public sealed class PortConventionTests
{
    /// <summary><c>AppTemplate.Application.Features.&lt;Vertical&gt;.Ports</c>.</summary>
    private const string _featurePortNamespacePattern = @"^AppTemplate\.Application\.Features\.([^.]+)\.Ports$";

    private const string _crossCuttingPortNamespace = "AppTemplate.Application.Common.Abstractions";

    /// <summary>
    /// The most operations one port may declare. Four is what the widest port here needs — issue,
    /// rotate, revoke, revoke-all, which are one mechanism and would be pointless apart. A port that
    /// wants a fifth is covering a second capability and belongs split in two.
    /// </summary>
    private const int _maximumOperationsPerPort = 4;

    /// <summary>
    /// Interfaces in the application layer that are not ports for a module to satisfy: the marker
    /// registration discovers use cases through, the use-case contracts themselves, and the
    /// domain-event consumer, which the application layer <em>implements</em> rather than consumes.
    /// </summary>
    private static readonly Type[] _notPorts =
    [
        typeof(IUseCase),
        typeof(IUseCase<>),
        typeof(IUseCase<,>),
        typeof(IDomainEventConsumer),
        typeof(IDomainEventConsumer<>),
    ];

    /// <summary>
    /// Every interface the application layer declares for something else to implement, discovered
    /// from the namespaces the convention puts them in rather than from a list a change could forget.
    /// </summary>
    private static IReadOnlyList<Type> Ports { get; } =
        [.. ArchitectureAssemblies.Application
            .GetTypes()
            .Where(type => type is { IsInterface: true, IsPublic: true, IsNested: false })
            .Where(type => !_notPorts.Contains(type.IsGenericType ? type.GetGenericTypeDefinition() : type))
            .Where(type => type.Namespace is not null
                && (IsFeaturePortNamespace(type.Namespace)
                    || string.Equals(type.Namespace, _crossCuttingPortNamespace, StringComparison.Ordinal)))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)];

    /// <summary>
    /// Every use case, with the ports it takes in its constructor. This is what "who depends on what"
    /// means for a use case: its constructor is its whole dependency list.
    /// </summary>
    private static IReadOnlyList<UseCaseDependencies> UseCases { get; } =
        [.. ArchitectureAssemblies.Application
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IUseCase).IsAssignableFrom(type))
            .Select(type => new UseCaseDependencies(type, DependenciesOf(type)))
            .OrderBy(useCase => useCase.Type.FullName, StringComparer.Ordinal)];

    [Fact]
    public void NoApplicationPort_IsAMultiCapabilityFacade()
    {
        Ports.Count.ShouldBeGreaterThanOrEqualTo(
            8,
            "Fewer ports were found than the application layer is known to declare: a unit of work, " +
            "a clock, a current user, an email sender, a to-do list query service and the " +
            "authentication capabilities. The discovery in this rule has stopped matching them.");

        Ports
            .Where(port => OperationsOf(port).Count > _maximumOperationsPerPort)
            .Select(port => $"{port.FullName} declares {OperationsOf(port).Count} operations")
            .Order(StringComparer.Ordinal)
            .ShouldBeEmpty(
                $"A port is one capability, and no more than {_maximumOperationsPerPort} operations " +
                "wide. A wider one is a façade: an implementer is forced to carry operations it has " +
                "no use for, and the caller stops sequencing anything because the port already does.");
    }

    /// <summary>
    /// Proves the counter above can fail, by applying it to an interface written here to be too wide.
    /// If this passed, the operation count would be evaluating nothing.
    /// </summary>
    [Fact]
    public void TheFacadeRule_IsSensitive_AndDetectsAWidePort()
    {
        OperationsOf(typeof(IDeliberatelyWidePort)).Count.ShouldBeGreaterThan(
            _maximumOperationsPerPort,
            $"{nameof(IDeliberatelyWidePort)} exists to be wider than the bound. That the counter " +
            "does not see it that way means it is not counting operations at all, and " +
            $"{nameof(NoApplicationPort_IsAMultiCapabilityFacade)} is decorative.");
    }

    /// <summary>
    /// A port with no consumer in the application layer is a contract the layer declares, a module
    /// implements, and nothing there uses — which means the decision it exists to serve is being made
    /// on the far side of it. <c>IEmailSender</c> was exactly that for as long as the only thing that
    /// sent mail lived in an infrastructure module.
    /// </summary>
    [Fact]
    public void EveryApplicationPort_HasAConsumerInTheApplicationLayer()
    {
        Ports.ShouldNotBeEmpty();

        var consumed = ArchitectureAssemblies.Application
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .SelectMany(DependenciesOf)
            .ToHashSet();

        consumed.ShouldNotBeEmpty(
            "No class in the application layer takes a dependency in its constructor, which cannot " +
            "be right and would make this rule pass for the wrong reason.");

        Ports
            .Where(port => !consumed.Contains(port))
            .Select(port => port.FullName ?? port.Name)
            .Order(StringComparer.Ordinal)
            .ShouldBeEmpty(
                "A port the application layer declares but never consumes is a decision that has " +
                "moved out of this layer. Either something here should be calling it, or it does not " +
                "belong to this layer at all.");
    }

    /// <summary>
    /// The façade shape read from the other side: a port every use case in its vertical depends on is
    /// that vertical's single collaborator, and the use cases are then wrappers around it whatever
    /// its method count.
    /// <para>
    /// Restricted to feature ports. A cross-cutting one — the clock, the caller's identity — may
    /// legitimately be needed by everything, and that is not what a façade is.
    /// </para>
    /// </summary>
    [Fact]
    public void NoFeaturePort_IsADependencyOfEveryUseCaseInItsVertical()
    {
        var verticals = UseCases
            .Select(useCase => VerticalOf(useCase.Type))
            .Where(vertical => vertical is not null)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        verticals.Count.ShouldBeGreaterThanOrEqualTo(
            2,
            "Fewer than two verticals were found, so the discovery has stopped reading the " +
            "Features/<Vertical> layout and this rule is guarding nothing.");

        var offenders = new List<string>();
        int verticalsChecked = 0;

        foreach (string? vertical in verticals)
        {
            var useCases = UseCases
                .Where(useCase => string.Equals(VerticalOf(useCase.Type), vertical, StringComparison.Ordinal))
                .ToList();

            var featurePorts = Ports
                .Where(port => string.Equals(VerticalOf(port), vertical, StringComparison.Ordinal))
                .ToList();

            // A vertical with one use case cannot exhibit the shape, and one with no port of its own
            // has nothing to check.
            if (useCases.Count < 2 || featurePorts.Count == 0)
            {
                continue;
            }

            verticalsChecked++;

            offenders.AddRange(
                featurePorts
                    .Where(port => useCases.TrueForAll(useCase => useCase.Dependencies.Contains(port)))
                    .Select(port =>
                        $"{port.FullName} is taken by all {useCases.Count} use cases in '{vertical}'"));
        }

        verticalsChecked.ShouldBeGreaterThan(
            0,
            "No vertical had both several use cases and a port of its own, so the condition below " +
            "was never evaluated.");

        offenders.ShouldBeEmpty(
            "A port that every use case in its vertical depends on is that vertical's façade, and " +
            "the use cases are wrappers around it. Split it by capability so that each use case " +
            "names only the capabilities it actually sequences.");
    }

    /// <summary>
    /// The point of splitting the façade, stated as a rule: a use case that takes one collaborator
    /// and hands the whole request to it decides nothing, and testing it tests the mock.
    /// <para>
    /// Counted rather than named, and asserted over the authentication vertical because that is where
    /// the shape was: several of its operations are genuinely one step — signing out revokes one
    /// grant — so the rule is that <em>most</em> of a vertical's use cases sequence more than one
    /// collaborator, not that all of them do.
    /// </para>
    /// </summary>
    [Fact]
    public void MostAuthUseCases_SequenceMoreThanOneCollaborator()
    {
        var authUseCases = UseCases
            .Where(useCase => string.Equals(VerticalOf(useCase.Type), "Auth", StringComparison.Ordinal))
            .ToList();

        authUseCases.Count.ShouldBe(
            6,
            "The authentication vertical has six use cases. Finding another number means this rule " +
            "is no longer describing it.");

        // The validator every use case takes is not a collaborator it sequences.
        var orchestrating = authUseCases
            .Where(useCase => useCase.Dependencies.Count(Ports.Contains) > 1)
            .ToList();

        orchestrating.Count.ShouldBeGreaterThanOrEqualTo(
            4,
            "Only " + orchestrating.Count + " of the six authentication use cases take more than one " +
            "port. Register, log in, refresh and resend each sequence several capabilities — a " +
            "single collaborator for those means the sequencing has moved back behind a port, and " +
            "there is nothing in this layer left to test.");
    }

    private static bool IsFeaturePortNamespace(string @namespace) =>
        Regex.IsMatch(@namespace, _featurePortNamespacePattern, RegexOptions.None, TimeSpan.FromSeconds(5));

    /// <summary>
    /// The vertical a type belongs to, read from <c>AppTemplate.Application.Features.&lt;Vertical&gt;.…</c>.
    /// </summary>
    private static string? VerticalOf(Type type)
    {
        const string prefix = "AppTemplate.Application.Features.";

        if (type.Namespace is null || !type.Namespace.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        string remainder = type.Namespace[prefix.Length..];
        int separator = remainder.IndexOf('.', StringComparison.Ordinal);

        return separator < 0 ? remainder : remainder[..separator];
    }

    /// <summary>
    /// An interface's own operations. Property accessors are excluded — a port exposing a value is
    /// not offering an operation an implementer has to carry — and so are members inherited from a
    /// base interface, which <see cref="Type.GetMethods()"/> does not report for interfaces anyway.
    /// </summary>
    private static IReadOnlyList<MethodInfo> OperationsOf(Type port) =>
        [.. port.GetMethods().Where(method => !method.IsSpecialName)];

    /// <summary>
    /// The interface types a class asks for in its constructors. Concrete parameters are ignored:
    /// this rule is about which contracts a type is written against.
    /// </summary>
    private static IReadOnlyList<Type> DependenciesOf(Type type) =>
        [.. type.GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .Where(parameterType => parameterType.IsInterface)
            .Distinct()];

    private sealed record UseCaseDependencies(Type Type, IReadOnlyList<Type> Dependencies);
}

/// <summary>
/// Five operations spanning three unrelated capabilities, written to be exactly what
/// <see cref="PortConventionTests.NoApplicationPort_IsAMultiCapabilityFacade"/> forbids. It lives
/// here, in the test project, so the sensitivity proof needs no violation in the product code.
/// </summary>
internal interface IDeliberatelyWidePort
{
    Task CreateAsync(CancellationToken cancellationToken = default);

    Task VerifyAsync(CancellationToken cancellationToken = default);

    Task IssueAsync(CancellationToken cancellationToken = default);

    Task RevokeAsync(CancellationToken cancellationToken = default);

    Task SendAsync(CancellationToken cancellationToken = default);
}

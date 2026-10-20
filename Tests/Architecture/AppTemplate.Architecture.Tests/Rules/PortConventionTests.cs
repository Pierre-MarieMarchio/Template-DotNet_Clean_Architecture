using System.Reflection;
using System.Text.RegularExpressions;
using AppTemplate.Application.Core.Common.UseCases;
using AppTemplate.Architecture.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class PortConventionTests
{
    private const string _featurePortNamespaceSuffixPattern =
        @"\.Features\.([^.]+)\.Ports(\.[^.]+)*$";

    private const string _crossCuttingPortNamespaceSuffix = ".Common.Ports";

    private const int _maximumOperationsPerPort = 6;

    private static IReadOnlyList<Type> Ports { get; } =
        [.. ApplicationLayerTypes
            .Where(type => type is { IsInterface: true, IsPublic: true, IsNested: false })
            .Where(type => IsFeaturePortNamespace(type) || IsCrossCuttingPortNamespace(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)];

    private static IReadOnlyList<UseCaseDependencies> UseCases { get; } =
        [.. ApplicationLayerTypes
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IUseCase).IsAssignableFrom(type))
            .Select(type => new UseCaseDependencies(type, DependenciesOf(type)))
            .OrderBy(useCase => useCase.Type.FullName, StringComparer.Ordinal)];

    [Fact]
    public void NoApplicationPort_IsAMultiCapabilityFacade()
    {
        Ports.Count.ShouldBeGreaterThanOrEqualTo(
            6,
            "Fewer ports were found than the application layer declares even with no feature and no " +
            "accounts: a unit of work, a clock, a current user and an email sender among them. The " +
            "discovery in this rule has stopped matching them.");

        Ports
            .Where(port => OperationsOf(port).Count > _maximumOperationsPerPort)
            .Select(port => $"{port.FullName} declares {OperationsOf(port).Count} operations")
            .Order(StringComparer.Ordinal)
            .ShouldBeEmpty(
                $"A port is one capability, and no more than {_maximumOperationsPerPort} operations " +
                "wide. A wider one is a façade: an implementer is forced to carry operations it has " +
                "no use for, and the caller stops sequencing anything because the port already does.");
    }

    [Fact]
    public void TheFacadeRule_IsSensitive_AndDetectsAWidePort()
    {
        OperationsOf(typeof(IDeliberatelyWidePort)).Count.ShouldBeGreaterThan(
            _maximumOperationsPerPort,
            $"{nameof(IDeliberatelyWidePort)} exists to be wider than the bound. That the counter " +
            "does not see it that way means it is not counting operations at all, and " +
            $"{nameof(NoApplicationPort_IsAMultiCapabilityFacade)} is decorative.");
    }

    private static readonly string[] _awaitingTheirFirstConsumer =
    [
        /*#if (tagging)*/
        /*#else
        "ICacheStore",
        #endif*/
        /*#if (reminders || files)*/
        /*#else
        "ILeaderLease",
        #endif*/
        /*#if (anyFeature)*/
        /*#else
        "IUnitOfWork",
        #endif*/
        /*#if (localAuth)*/
        /*#else
        "ICurrentUser",
        "ICurrentAccessToken",
        "IEmailSender",
        #endif*/
        /*#if (accounts)*/
        /*#else
        "IDomainEventPublisher",
        #endif*/
    ];

    [Fact]
    public void EveryApplicationPort_HasAConsumerInTheApplicationLayer()
    {
        Ports.ShouldNotBeEmpty();

        var consumed = ApplicationLayerTypes
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .SelectMany(DependenciesOf)
            .ToHashSet();

        consumed.ShouldNotBeEmpty(
            "No class in the application layer takes a dependency in its constructor, which cannot " +
            "be right and would make this rule pass for the wrong reason.");

        Ports
            .Where(port => !consumed.Contains(port))
            .Where(port => !_awaitingTheirFirstConsumer.Contains(port.Name, StringComparer.Ordinal))
            .Select(port => port.FullName ?? port.Name)
            .Order(StringComparer.Ordinal)
            .ShouldBeEmpty(
                "A port the application layer declares but never consumes is a decision that has " +
                "moved out of this layer. Either something here should be calling it, or it does not " +
                "belong to this layer at all.");
    }

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
            1,
            "No vertical was found, so the discovery has stopped reading the Features/<Vertical> " +
            "layout and this rule is guarding nothing.");

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

            if (useCases.Count < 2 || featurePorts.Count < 2)
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

        //#if (localAuth)
        verticalsChecked.ShouldBeGreaterThan(
            0,
            "No vertical had both several use cases and several ports of its own, so the condition below " +
            "was never evaluated.");
        //#endif

        offenders.ShouldBeEmpty(
            "A port that every use case in its vertical depends on is that vertical's façade, and " +
            "the use cases are wrappers around it. Split it by capability so that each use case " +
            "names only the capabilities it actually sequences.");
    }
    //#if (localAuth)

    private static readonly string[] _authUseCasesPerFeature =
    [
        "Accounts: 15",
        "ExternalLogins: 1",
        "Passkeys: 6",
        "SecurityEvents: 3",
        "Sessions: 8",
        "TwoFactor: 5",
    ];

    [Fact]
    public void MostAuthUseCases_SequenceMoreThanOneCollaborator()
    {
        var authUseCases = UseCases
            .Where(useCase => string.Equals(
                useCase.Type.Assembly.GetName().Name,
                "AppTemplate.Application.Auth",
                StringComparison.Ordinal))
            .ToList();

        authUseCases
            .GroupBy(useCase => VerticalOf(useCase.Type) ?? "(outside Features/)", StringComparer.Ordinal)
            .Select(feature => $"{feature.Key}: {feature.Count()}")
            .Order(StringComparer.Ordinal)
            .ShouldBe(
                _authUseCasesPerFeature,
                ignoreOrder: false,
                "The authentication module's thirty-eight use cases are filed under six features — " +
                "account lifecycle and administration, sessions, two-factor, passkeys, external " +
                "sign-in and the security trail. Another distribution means an operation moved, " +
                "appeared or vanished, and this rule is no longer describing the module.");

        var orchestrating = authUseCases
            .Where(useCase => useCase.Dependencies.Count(Ports.Contains) > 1)
            .ToList();

        orchestrating.Count.ShouldBeGreaterThanOrEqualTo(
            4,
            "Only " + orchestrating.Count + " of the authentication use cases take more than one " +
            "port. Register, log in, refresh and resend each sequence several capabilities — a " +
            "single collaborator for those means the sequencing has moved back behind a port, and " +
            "there is nothing in this layer left to test.");
    }
    //#endif

    private static IEnumerable<Type> ApplicationLayerTypes =>
        ArchitectureAssemblies.ApplicationLayer.SelectMany(assembly => assembly.GetTypes());

    private static string RootOf(Type type) =>
        type.Assembly.GetName().Name
        ?? throw new InvalidOperationException(
            $"'{type.FullName}' is declared in an assembly with no simple name.");

    private static bool IsFeaturePortNamespace(Type type) =>
        type.Namespace is { } declared
        && Regex.IsMatch(
            declared,
            $"^{Regex.Escape(RootOf(type))}{_featurePortNamespaceSuffixPattern}",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

    private static bool IsCrossCuttingPortNamespace(Type type) =>
        string.Equals(
            type.Namespace,
            RootOf(type) + _crossCuttingPortNamespaceSuffix,
            StringComparison.Ordinal);

    private static string? VerticalOf(Type type)
    {
        string prefix = RootOf(type) + ".Features.";

        if (type.Namespace is null || !type.Namespace.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        string remainder = type.Namespace[prefix.Length..];
        int separator = remainder.IndexOf('.', StringComparison.Ordinal);

        return separator < 0 ? remainder : remainder[..separator];
    }

    private static IReadOnlyList<MethodInfo> OperationsOf(Type port) =>
        [.. port.GetMethods().Where(method => !method.IsSpecialName)];

    private static IReadOnlyList<Type> DependenciesOf(Type type) =>
        [.. type.GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .SelectMany(ContractsOf)
            .Distinct()];

    private static IEnumerable<Type> ContractsOf(Type parameterType)
    {
        if (parameterType is { IsGenericType: true }
            && parameterType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            return parameterType.GetGenericArguments()[0] is { IsInterface: true } port ? [port] : [];
        }

        return parameterType.IsInterface ? [parameterType] : [];
    }

    private sealed record UseCaseDependencies(Type Type, IReadOnlyList<Type> Dependencies);
}

internal interface IDeliberatelyWidePort
{
    Task CreateAsync(CancellationToken cancellationToken = default);

    Task VerifyAsync(CancellationToken cancellationToken = default);

    Task IssueAsync(CancellationToken cancellationToken = default);

    Task RevokeAsync(CancellationToken cancellationToken = default);

    Task SendAsync(CancellationToken cancellationToken = default);

    Task RenderAsync(CancellationToken cancellationToken = default);

    Task PurgeAsync(CancellationToken cancellationToken = default);
}

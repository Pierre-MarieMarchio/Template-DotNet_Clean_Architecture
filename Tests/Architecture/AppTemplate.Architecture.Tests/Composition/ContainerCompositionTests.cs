using System.Collections;
using AppTemplate.Application;
using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Auth.Ports.RefreshTokenMaintenance;
using AppTemplate.Application.Features.Auth.Ports.UserProfiles;
using AppTemplate.Architecture.Tests.Fixtures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Composition;

/// <summary>
/// The container the API composes must be able to satisfy every registration in it. A registration
/// that compiles but cannot be resolved gives a green build, a clean start-up and a broken request.
/// <para>
/// Three layers of check, deliberately overlapping. <c>ValidateOnBuild</c> catches a constructor
/// dependency nothing satisfies. <c>ValidateScopes</c> catches a scoped service captured by a
/// singleton. And the walk over <see cref="IServiceCollection"/> catches what neither can see:
/// a factory-based registration, which <c>ValidateOnBuild</c> does not inspect at all.
/// </para>
/// </summary>
public sealed class ContainerCompositionTests
{
    /// <summary>
    /// The ports the application layer declares. Each is satisfied by exactly one adapter in
    /// exactly one module, and none of them may be missing from a composed host. Discovered rather
    /// than listed: the eleven written out here by hand were a third of the real number.
    /// </summary>
    private static IReadOnlyList<Type> ApplicationPortContracts => ApplicationPorts.All;

    [Fact]
    public void TheApiContainer_BuildsUnderStrictValidation()
    {
        var services = HostComposition.ComposeApi(HostComposition.Configuration());

        using var provider = services.BuildServiceProvider(HostComposition.StrictValidation);

        // Program.cs never calls this explicitly: the host does it during start-up. Calling it here
        // is what makes the ValidateOnStart on every options section actually run, so a bad signing
        // key or an unreachable confirmation URL fails this test rather than the first login.
        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    [Fact]
    public void TheApiContainer_ResolvesEveryRegisteredService()
    {
        var services = HostComposition.ComposeApi(HostComposition.Configuration());

        AssertEveryServiceResolves(
            services,
            "A registration that compiles but cannot be resolved is the exact failure this project " +
            "exists to prevent: green build, clean start-up, broken request.");
    }

    /// <summary>
    /// The same walk over the test-host composition. <c>AppTemplate.Infrastructure.InMemory</c> replaces two
    /// adapters by removing and re-adding them, which is a registration edit — and an edit that left
    /// the container unresolvable would otherwise only be discovered by an integration test.
    /// </summary>
    [Fact]
    public void TheTestHostContainer_WithTheInMemoryModule_ResolvesEveryRegisteredService()
    {
        var services = HostComposition.ComposeTestHost(HostComposition.Configuration());

        AssertEveryServiceResolves(
            services,
            "AddInMemoryModule replaces the clock and the email sender by removing and re-adding " +
            "them. If that leaves the graph unresolvable, every test host is broken.");
    }

    /// <summary>
    /// Every use case the application assembly declares, discovered rather than listed: a list would
    /// only ever guard what somebody remembered to add to it, which is the weakness the discovery in
    /// <c>ApplicationModule</c> exists to remove.
    /// <para>
    /// Each one is resolved through its own named interface and must come back as the concrete class
    /// that declares it, so a contract bound to the wrong implementation fails here too.
    /// </para>
    /// <para>
    /// The name promises <em>every</em>, so the population is measured rather than floored. A number
    /// written here could not be wrong, only stale — and while it was stale this test would resolve
    /// a handful of use cases and report that all of them were fine.
    /// </para>
    /// </summary>
    [Fact]
    public void TheApiContainer_ResolvesEveryUseCaseInTheApplicationAssembly()
    {
        var implementations = UseCaseTypes.InApplicationAssembly;

        SourceDeclarations.WalkComplaints.ShouldBeEmpty(
            "The source-tree walk this test measures its expectation with did not read the tree it " +
            $"describes, so the comparison below would hold by comparing nothing.{Environment.NewLine}  " +
            string.Join($"{Environment.NewLine}  ", SourceDeclarations.WalkComplaints));

        var divergence = SourceDeclarations.Divergence(
            SourceDeclarations.UseCaseFullNames,
            implementations.Select(SourceDeclarations.WithoutArity),
            "found by the IUseCase discovery this test resolves through the container");

        divergence.ShouldBeEmpty(
            "This test promises to resolve every use case, so the population it walks has to be the " +
            "population the source tree declares. The two were measured independently — " +
            $"{SourceDeclarations.UseCaseFullNames.Count} declared in the application project, " +
            $"{implementations.Count} found by reflection — and they no longer agree, so one of the " +
            $"two has stopped matching:{Environment.NewLine}  " +
            string.Join($"{Environment.NewLine}  ", divergence));

        var services = HostComposition.ComposeApi(HostComposition.Configuration());

        using var provider = services.BuildServiceProvider(HostComposition.StrictValidation);
        using var scope = provider.CreateScope();

        var failures = new List<string>();

        foreach (var implementation in implementations)
        {
            try
            {
                scope.ServiceProvider
                    .GetRequiredService(UseCaseTypes.ContractOf(implementation))
                    .ShouldBeOfType(implementation);
            }
            catch (Exception exception)
            {
                failures.Add($"{implementation.FullName}: {exception.GetType().Name}: {exception.Message}");
            }
        }

        failures.ShouldBeEmpty(
            $"A use case exists that the composed host cannot hand to a controller.{Environment.NewLine}  " +
            string.Join($"{Environment.NewLine}  ", failures));
    }

    [Fact]
    public void TheApiContainer_ResolvesEveryApplicationPort()
    {
        AssertResolvable(
            ApplicationPortContracts,
            "Each port has exactly one adapter in exactly one module. A port with no registration " +
            "is a hole the compiler cannot see, because the port and the adapter live in different " +
            "assemblies and only the host puts them together.");
    }

    /// <summary>
    /// Non-vacuity for the discovery that replaced two hand-written arrays. If
    /// <see cref="ApplicationPorts"/> ever stopped matching — a renamed folder, a namespace that no
    /// longer starts the way the convention says — every rule reading it would pass by inspecting
    /// nothing, which is precisely the failure the hand-written lists were suffering from silently.
    /// </summary>
    [Fact]
    public void ThePortDiscovery_FindsEveryPortTheRepositoryHas()
    {
        SourceDeclarations.WalkComplaints.ShouldBeEmpty(
            "The source-tree walk both comparisons below are measured against did not read the tree " +
            $"it describes.{Environment.NewLine}  " +
            string.Join($"{Environment.NewLine}  ", SourceDeclarations.WalkComplaints));

        var repositories = SourceDeclarations.Divergence(
            SourceDeclarations.DomainRepositoryFullNames,
            ApplicationPorts.DomainRepositories.Select(SourceDeclarations.WithoutArity),
            "found by the repository discovery");

        repositories.ShouldBeEmpty(
            "One repository contract per aggregate, and the discovery has to find the same set the " +
            "Domain's Repositories folders declare — measured, not listed, so a new aggregate needs " +
            $"no edit here:{Environment.NewLine}  " +
            string.Join($"{Environment.NewLine}  ", repositories));

        // Declared is the port namespaces minus the markers, so the markers go back in to compare
        // like with like: what the walk reads off disk is every public interface declared there.
        var declaredAndExcluded = ApplicationPorts.Declared
            .Concat(ApplicationPorts.NotPorts)
            .Select(SourceDeclarations.WithoutArity);

        var ports = SourceDeclarations.Divergence(
            SourceDeclarations.PortFullNames,
            declaredAndExcluded,
            "seen by the port discovery");

        ports.ShouldBeEmpty(
            "Every public interface under a vertical's Ports folder or under Common is either a port " +
            "this discovery declares or one it names as a marker. A third outcome means the " +
            "namespace match has stopped following the convention, and every rule reading it is " +
            $"inspecting less than it says. ({SourceDeclarations.PortFullNames.Count} declared on " +
            $"disk, {ApplicationPorts.Declared.Count} ports plus " +
            $"{ApplicationPorts.NotPorts.Count} markers discovered.){Environment.NewLine}  " +
            string.Join($"{Environment.NewLine}  ", ports));
    }

    [Fact]
    public void TheWorkerContainer_BuildsUnderStrictValidation()
    {
        var services = HostComposition.ComposeWorker(HostComposition.Configuration());

        Should.NotThrow(
            () => services.BuildServiceProvider(HostComposition.StrictValidation).Dispose(),
            "The worker composes the same four modules as the API but supplies no " +
            "IHttpContextAccessor. A module that grew a dependency on one would start the API and " +
            "stop this host, and only this test would say so.");
    }

    [Fact]
    public void TheWorkerContainer_ResolvesEveryApplicationPort()
    {
        AssertResolvable(
            ApplicationPortContracts,
            "The worker runs FireDueReminders and both purges through the same ports the API uses. " +
            "A port only the API's own composition could satisfy would fail here.",
            HostComposition.ComposeWorker);
    }

    /// <summary>
    /// Pins <em>why</em> the worker composes the identity module, because eight comments in this
    /// repository name the wrong reason and every one of them stayed true-looking for months.
    /// <para>
    /// The written reason is <c>IRefreshTokenMaintenanceService</c>'s sole adapter, which invites
    /// the conclusion that moving that one adapter would let this host drop the module and its
    /// whole configuration surface with it. It would not. <c>EmailReminderNotifier</c> — the
    /// adapter behind the reminder loop, which is this host's own feature and not a favour to the
    /// API — resolves <see cref="IUserProfilesService"/> to find the address a due reminder is
    /// rung at. And <c>AddApplicationLayer</c> registers every use case in the assembly, so under
    /// <c>ValidateOnBuild</c> — which <c>Host.CreateApplicationBuilder</c> turns on in Development
    /// — every port the layer declares has to be resolvable in every host, not merely the ports
    /// this host's own loops reach.
    /// </para>
    /// </summary>
    [Fact]
    public void TheWorkerContainer_NeedsIdentityForItsReminderLoop_NotOnlyForThePurgeAdapter()
    {
        var services = HostComposition.ComposeWorkerWithoutTheIdentityModule(HostComposition.Configuration());

        var exception = Should.Throw<AggregateException>(
            () => services.BuildServiceProvider(HostComposition.StrictValidation).Dispose());

        exception.Message.ShouldContain(
            nameof(IUserProfilesService),
            customMessage: "If this port ever stops being needed here, the worker really can drop " +
            "AddIdentityModule — and the comments in Program.cs, the csproj, docker-compose.yml, " +
            "configmap-worker.yaml and docs/CONFIGURATION.md all become answerable at last.");

        exception.Message.ShouldContain(
            nameof(IRefreshTokenMaintenanceService),
            customMessage: "The written reason is real too, just not the only one — if it were, " +
            "moving that adapter would be worth doing.");
    }

    // ---- Proof that the checks above can fail -------------------------------------------------

    /// <summary>
    /// Drops one module and asserts the container refuses to build. The application layer's sign-up
    /// use cases depend on <see cref="IEmailSender"/>, which no module but the email one implements,
    /// so the host is the only thing that can complete the graph.
    /// <para>
    /// If this test passed, <c>ValidateOnBuild</c> would be inspecting nothing and
    /// <see cref="TheApiContainer_ResolvesEveryRegisteredService"/> would be incapable of failing.
    /// </para>
    /// </summary>
    [Fact]
    public void TheContainer_RefusesToBuild_WhenAModuleIsNotComposed()
    {
        var services = HostComposition.ComposeApiWithoutTheEmailModule(HostComposition.Configuration());

        var exception = Should.Throw<AggregateException>(
            () => services.BuildServiceProvider(HostComposition.StrictValidation).Dispose());

        // The diagnostic must name the missing port, or it is not actionable.
        exception.Message.ShouldContain(nameof(IEmailSender));
    }

    /// <summary>
    /// A use case with no interface of its own has no service type to bind. Registration must say so
    /// while the process is starting, not bind nothing and leave the first request to discover it.
    /// </summary>
    [Fact]
    public void TheRegistration_RefusesAUseCaseWithNoNamedInterface()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => new ServiceCollection().AddUseCases([typeof(UseCaseWithNoContract)]));

        exception.Message.ShouldContain(typeof(UseCaseWithNoContract).FullName!);
    }

    /// <summary>
    /// Two candidate interfaces are as ambiguous as none: picking one would be a guess that a
    /// controller then depends on.
    /// </summary>
    [Fact]
    public void TheRegistration_RefusesAUseCaseWithTwoNamedInterfaces()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => new ServiceCollection().AddUseCases([typeof(UseCaseWithTwoContracts)]));

        exception.Message.ShouldContain(typeof(UseCaseWithTwoContracts).FullName!);
        exception.Message.ShouldContain(nameof(IFirstContract));
        exception.Message.ShouldContain(nameof(ISecondContract));
    }

    /// <summary>
    /// Proves the options half of the guarantee: every section is validated with
    /// <c>ValidateOnStart</c>, so a signing key too short for HS256 stops the process instead of
    /// producing tokens nothing can verify.
    /// </summary>
    [Fact]
    public void TheContainer_RefusesToStart_WhenAnOptionsSectionIsInvalid()
    {
        var configuration = HostComposition.Configuration(
            new KeyValuePair<string, string?>("Jwt:Key", "too-short"));

        var services = HostComposition.ComposeApi(configuration);

        using var provider = services.BuildServiceProvider(HostComposition.StrictValidation);

        var exception = Should.Throw<OptionsValidationException>(
            provider.GetRequiredService<IStartupValidator>().Validate);

        exception.Message.ShouldContain("Jwt:Key");
    }

    // ---- Helpers -------------------------------------------------------------------------------

    /// <summary>
    /// Resolves every non-open-generic service descriptor inside a scope.
    /// <para>
    /// Both directly and through <c>IEnumerable&lt;T&gt;</c>: the direct resolution proves the
    /// registration a caller would get is constructible, and the enumerable resolution constructs
    /// <em>every</em> registration for the service rather than only the last one to win — which is
    /// where a duplicate or shadowed registration hides.
    /// </para>
    /// </summary>
    private static void AssertEveryServiceResolves(IServiceCollection services, string because)
    {
        var descriptors = services.ToList();

        descriptors.Count.ShouldBeGreaterThan(
            100,
            "The composed host is expected to hold hundreds of descriptors (Identity, JWT bearer, " +
            "the context, the interceptor pipeline, the application layer). Far fewer means the " +
            "composition failed quietly and this test is walking an almost-empty collection.");

        using var provider = services.BuildServiceProvider(HostComposition.StrictValidation);
        using var scope = provider.CreateScope();

        var failures = new List<string>();
        var openGenerics = new List<string>();
        int resolved = 0;

        foreach (var descriptor in descriptors)
        {
            var serviceType = descriptor.ServiceType;

            // An open generic has no instance to resolve until it is closed; ILogger<T> and
            // IOptions<T> are registered this way and are exercised through their consumers.
            if (serviceType.ContainsGenericParameters)
            {
                openGenerics.Add(serviceType.Name);
                continue;
            }

            // A registration under KeyedService.AnyKey is a catch-all with no key to ask for.
            if (descriptor.IsKeyedService && ReferenceEquals(descriptor.ServiceKey, KeyedService.AnyKey))
            {
                continue;
            }

            try
            {
                if (descriptor.IsKeyedService)
                {
                    scope.ServiceProvider
                        .GetRequiredKeyedService(serviceType, descriptor.ServiceKey)
                        .ShouldNotBeNull();
                }
                else
                {
                    scope.ServiceProvider.GetRequiredService(serviceType).ShouldNotBeNull();

                    var enumerableType = typeof(IEnumerable<>).MakeGenericType(serviceType);

                    foreach (object? instance in (IEnumerable)scope.ServiceProvider.GetRequiredService(enumerableType))
                    {
                        instance.ShouldNotBeNull();
                    }
                }

                resolved++;
            }
            catch (Exception exception)
            {
                failures.Add(
                    $"{serviceType.FullName} ({descriptor.Lifetime}): " +
                    $"{exception.GetType().Name}: {exception.Message}");
            }
        }

        resolved.ShouldBeGreaterThan(
            50,
            $"Only {resolved} services were resolved out of {descriptors.Count} descriptors " +
            $"({openGenerics.Count} open generics skipped), which is too few to be a real check.");

        failures.ShouldBeEmpty(
            $"{because}{Environment.NewLine}Unresolvable registrations:{Environment.NewLine}  " +
            string.Join($"{Environment.NewLine}  ", failures));
    }

    private static void AssertResolvable(
        IReadOnlyList<Type> serviceTypes,
        string because,
        Func<IConfiguration, ServiceCollection>? compose = null)
    {
        serviceTypes.ShouldNotBeEmpty();

        var services = (compose ?? HostComposition.ComposeApi)(HostComposition.Configuration());

        using var provider = services.BuildServiceProvider(HostComposition.StrictValidation);
        using var scope = provider.CreateScope();

        var failures = new List<string>();

        foreach (var serviceType in serviceTypes)
        {
            try
            {
                scope.ServiceProvider.GetRequiredService(serviceType).ShouldNotBeNull();
            }
            catch (Exception exception)
            {
                failures.Add($"{serviceType.FullName}: {exception.GetType().Name}: {exception.Message}");
            }
        }

        failures.ShouldBeEmpty(
            $"{because}{Environment.NewLine}Could not resolve:{Environment.NewLine}  " +
            string.Join($"{Environment.NewLine}  ", failures));
    }
}

/// <summary>
/// Finds use cases by type identity, derived here rather than read back from the container, so the
/// assertion describes what should be registered instead of what is.
/// </summary>
internal static class UseCaseTypes
{
    internal static IReadOnlyList<Type> InApplicationAssembly { get; } =
        [.. ArchitectureAssemblies.Application
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false }
                && typeof(IUseCase).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)];

    internal static Type ContractOf(Type implementation) =>
        implementation.GetInterfaces()
            .Where(candidate => candidate != typeof(IUseCase)
                && !candidate.IsGenericType
                && typeof(IUseCase).IsAssignableFrom(candidate))
            .ToArray() is [var contract]
            ? contract
            : throw new InvalidOperationException(
                $"'{implementation.FullName}' must declare exactly one named use-case interface.");
}

/// <summary>Declares the generic contract directly, so it has no service type of its own.</summary>
internal sealed class UseCaseWithNoContract : IUseCase<Guid, Result>
{
    public Task<Result> ExecuteAsync(Guid request, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());
}

internal interface IFirstContract : IUseCase<Guid, Result>;

internal interface ISecondContract : IUseCase<Guid, Result>;

/// <summary>Two candidate service types, neither of which registration may pick for the author.</summary>
internal sealed class UseCaseWithTwoContracts : IFirstContract, ISecondContract
{
    public Task<Result> ExecuteAsync(Guid request, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());
}

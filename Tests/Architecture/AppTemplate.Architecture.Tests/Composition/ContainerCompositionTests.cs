using System.Collections;
using AppTemplate.Application.Core;
//#if (localAuth)
using AppTemplate.Application.Core.Common.Ports;
//#endif
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Application.Core.Common.UseCases;
using AppTemplate.Architecture.Tests.Fixtures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Composition;

public sealed class ContainerCompositionTests
{
    /*#if (externalAuth)*/
    private static IReadOnlyList<Type> ApplicationPortContracts =>
        [.. ApplicationPorts.All.Where(port => !IsExternalIdentityPort(port))];

    private static IReadOnlyList<Type> ExternalIdentityPortContracts =>
        [.. ApplicationPorts.All.Where(IsExternalIdentityPort)];

    private static bool IsExternalIdentityPort(Type port) =>
        port.Namespace?.StartsWith($"{HostComposition.ExternalIdentityNamespace}.", StringComparison.Ordinal) == true;
    /*#else
    private static IReadOnlyList<Type> ApplicationPortContracts => ApplicationPorts.All;
    #endif*/

    [Fact]
    public void TheApiContainer_BuildsUnderStrictValidation()
    {
        var services = HostComposition.ComposeApi(HostComposition.Configuration());

        using var provider = services.BuildServiceProvider(HostComposition.StrictValidation);

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

    [Fact]
    public void TheTestHostContainer_WithTheInMemoryModule_ResolvesEveryRegisteredService()
    {
        var services = HostComposition.ComposeTestHost(HostComposition.Configuration());

        AssertEveryServiceResolves(
            services,
            "AddInMemoryModule replaces the clock and the email sender by removing and re-adding " +
            "them. If that leaves the graph unresolvable, every test host is broken.");
    }

    [Fact]
    public void TheApiContainer_ResolvesEveryUseCaseInTheApplicationAssembly()
    {
        var implementations = UseCaseTypes.InTheApplicationLayer;

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
            $"{SourceDeclarations.UseCaseFullNames.Count} declared across the application layer, " +
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
    public void TheBackgroundOnlyContainer_BuildsUnderStrictValidation()
    {
        var services = HostComposition.ComposeBackgroundOnly(HostComposition.Configuration());

        Should.NotThrow(
            () => services.BuildServiceProvider(HostComposition.StrictValidation).Dispose(),
            "A process that only runs the jobs composes the same modules as the API but supplies no " +
            "IHttpContextAccessor. A module that grew a dependency on one would start the API and " +
            "stop that process, and only this test would say so.");
    }

    //#if (externalAuth)
    [Fact]
    public void TheExternalIdentityComposition_ResolvesItsPorts()
    {
        ExternalIdentityPortContracts.Count.ShouldBeGreaterThanOrEqualTo(
            3,
            "The external-identity mapping declares a store, its reads by owner and the local role " +
            "assignments; fewer were found, so the namespace this rule filters on no longer matches.");

        AssertResolvable(
            ExternalIdentityPortContracts,
            "A host authenticating callers with an external provider maps each one through these " +
            "ports on every request; one with no adapter fails the first authenticated call.",
            HostComposition.ComposeApiWithExternalIdentity);

        AssertResolvable(
            ExternalIdentityPortContracts,
            "The reminder job reads a recipient's address through these ports, from a scope with no " +
            "request; one only the API's own composition could satisfy would fail there.",
            HostComposition.ComposeBackgroundOnlyWithExternalIdentity);
    }

    //#endif
    [Fact]
    public void TheBackgroundOnlyContainer_ResolvesEveryApplicationPort()
    {
        AssertResolvable(
            ApplicationPortContracts,
            "The jobs run through the same ports the API's controllers use. A port only the " +
            "API's own composition could satisfy would fail here.",
            HostComposition.ComposeBackgroundOnly);
    }
    //#if (localAuth)

    [Fact]
    public void TheBackgroundOnlyContainer_BuildsWithoutAuthentication()
    {
        var services = HostComposition.ComposeBackgroundOnlyWithoutAuthentication(HostComposition.Configuration());

        Should.NotThrow(
            () => services.BuildServiceProvider(HostComposition.StrictValidation).Dispose(),
            "A module the jobs compose has started depending on an authentication port again.");
    }

    [Fact]
    public void TheApiContainer_BuildsWithoutAuthentication()
    {
        var services = HostComposition.ComposeApiWithoutAuthentication(HostComposition.Configuration());

        Should.NotThrow(
            () => services.BuildServiceProvider(HostComposition.StrictValidation).Dispose(),
            "A module has started depending on an authentication port, so removing authentication " +
            "is no longer a matter of leaving its composition out.");
    }
    //#endif

    [Fact]
    public void TheContainer_RefusesToBuild_WhenAModuleIsNotComposed()
    {
        /*#if (localAuth)*/
        var services = HostComposition.ComposeApiWithoutTheEmailModule(HostComposition.Configuration());
        /*#else
        var services = HostComposition.ComposeApiWithoutThePersistenceModule(HostComposition.Configuration());
        #endif*/

        var exception = Should.Throw<AggregateException>(
            () => services.BuildServiceProvider(HostComposition.StrictValidation).Dispose());

        /*#if (localAuth)*/
        exception.Message.ShouldContain(nameof(IEmailSender));
        /*#else
        exception.Message.ShouldContain(nameof(AppTemplate.Application.Core.Common.Idempotency.IIdempotencyStore));
        #endif*/
    }

    [Fact]
    public void TheRegistration_RefusesAUseCaseWithNoNamedInterface()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => new ServiceCollection().AddUseCases([typeof(UseCaseWithNoContract)]));

        exception.Message.ShouldContain(typeof(UseCaseWithNoContract).FullName!);
    }

    [Fact]
    public void TheRegistration_RefusesAUseCaseWithTwoNamedInterfaces()
    {
        var exception = Should.Throw<InvalidOperationException>(
            () => new ServiceCollection().AddUseCases([typeof(UseCaseWithTwoContracts)]));

        exception.Message.ShouldContain(typeof(UseCaseWithTwoContracts).FullName!);
        exception.Message.ShouldContain(nameof(IFirstContract));
        exception.Message.ShouldContain(nameof(ISecondContract));
    }

    [Fact]
    public void TheContainer_RefusesToStart_WhenAnOptionsSectionIsInvalid()
    {
        /*#if (localAuth)*/
        var configuration = HostComposition.Configuration(
            new KeyValuePair<string, string?>("Jwt:Key", "too-short"));
        /*#else
        var configuration = HostComposition.Configuration(
            new KeyValuePair<string, string?>("Email:Host", ""));
        #endif*/

        var services = HostComposition.ComposeApi(configuration);

        using var provider = services.BuildServiceProvider(HostComposition.StrictValidation);

        var exception = Should.Throw<OptionsValidationException>(
            provider.GetRequiredService<IStartupValidator>().Validate);

        /*#if (localAuth)*/
        exception.Message.ShouldContain("Jwt:Key");
        /*#else
        exception.Message.ShouldContain("Email:Host");
        #endif*/
    }

    private static void AssertEveryServiceResolves(IServiceCollection services, string because)
    {
        var descriptors = services.ToList();

        descriptors.Count.ShouldBeGreaterThan(
            50,
            "The composed host is expected to hold dozens of descriptors even with no feature and no " +
            "accounts (the context, the interceptor pipeline, the application layer). Far fewer means " +
            "the composition failed quietly and this test is walking an almost-empty collection.");

        using var provider = services.BuildServiceProvider(HostComposition.StrictValidation);
        using var scope = provider.CreateScope();

        var failures = new List<string>();
        var openGenerics = new List<string>();
        int resolved = 0;

        foreach (var descriptor in descriptors)
        {
            var serviceType = descriptor.ServiceType;

            if (serviceType.ContainsGenericParameters)
            {
                openGenerics.Add(serviceType.Name);
                continue;
            }

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
            45,
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

internal static class UseCaseTypes
{
    internal static IReadOnlyList<Type> InTheApplicationLayer { get; } =
        [.. ArchitectureAssemblies.ApplicationLayer
            .SelectMany(assembly => assembly.GetTypes())
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

internal sealed class UseCaseWithNoContract : IUseCase<Guid, Result>
{
    public Task<Result> ExecuteAsync(Guid request, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());
}

internal interface IFirstContract : IUseCase<Guid, Result>;

internal interface ISecondContract : IUseCase<Guid, Result>;

internal sealed class UseCaseWithTwoContracts : IFirstContract, ISecondContract
{
    public Task<Result> ExecuteAsync(Guid request, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());
}

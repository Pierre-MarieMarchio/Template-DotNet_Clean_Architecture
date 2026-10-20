using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Architecture.Tests.Fixtures;
using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class AdapterVisibilityTests
{
    private static IReadOnlyList<Type> ApplicationPortContracts => ApplicationPorts.All;

    [Fact]
    public void Adapters_ImplementingAnApplicationPort_AreInternalToTheirModule()
    {
        var offenders = new List<string>();
        int adaptersFound = 0;

        foreach (var assembly in ArchitectureAssemblies.ProductionInfrastructure)
        {
            RuleAssertions.RequireTypes(assembly);

            foreach (var port in ApplicationPortContracts)
            {
                var adapters = Types.InAssembly(assembly)
                    .That()
                    .AreClasses()
                    .And()
                    .AreNotAbstract()
                    .And()
                    .ImplementInterface(port);

                var matched = adapters.GetTypes().ToList();

                if (matched.Count == 0)
                {
                    continue;
                }

                adaptersFound += matched.Count;

                var result = adapters.ShouldNot().BePublic().GetResult();

                if (!result.IsSuccessful)
                {
                    offenders.AddRange(
                        (result.FailingTypeNames ?? [])
                        .Select(name => $"{name} implements {port.Name} and is public"));
                }
            }
        }

        adaptersFound.ShouldBeGreaterThanOrEqualTo(
            7,
            "Fewer adapters were found than the modules are known to contain even with no feature " +
            "and no accounts: a unit of work, a clock and an email sender among them. The discovery " +
            "in this rule has stopped matching them.");

        offenders.ShouldBeEmpty(
            "An adapter is internal to its module. Only the port it implements is public, which is " +
            "what keeps replacing the implementation a change inside one project.");
    }

    [Fact]
    public void InMemoryDoubles_ArePublic_Deliberately()
    {
        var inMemory = ArchitectureAssemblies.InMemoryInfrastructure;

        inMemory.GetTypes()
            .Where(type => type is { IsClass: true, IsPublic: true })
            .Select(type => type.Name)
            .ShouldContain(
                "FixedDateTimeProvider",
                "A test host has to be able to move the clock, so this double is public by design " +
                "and is why AppTemplate.Infrastructure.InMemory is excluded from the adapter-visibility rule.");
    }

    [Fact]
    public void TheVisibilityRule_IsSensitive_AndDetectsAPublicAdapter()
    {
        var doubles = Types.InAssembly(ArchitectureAssemblies.InMemoryInfrastructure)
            .That()
            .AreClasses()
            .And()
            .AreNotAbstract()
            .And()
            .ImplementInterface(typeof(IDateTimeProvider));

        RuleAssertions.RequireTypes(doubles, "an IDateTimeProvider implementation in AppTemplate.Infrastructure.InMemory");

        doubles.ShouldNot()
            .BePublic()
            .GetResult()
            .ShouldDetectAViolation(
                "FixedDateTimeProvider is public, so applying 'ShouldNot().BePublic()' to it must " +
                "fail. That it passed means the condition is evaluating nothing, and the " +
                "adapter-visibility rule above is worthless.");
    }

    [Fact]
    public void ApplicationPorts_ArePublicInterfaces()
    {
        ApplicationPortContracts.ShouldNotBeEmpty();

        foreach (var port in ApplicationPortContracts)
        {
            port.IsInterface.ShouldBeTrue($"{port.FullName} is listed as a port but is not an interface.");
            port.IsPublic.ShouldBeTrue($"{port.FullName} is a port and must be public.");

            bool isRepository = IsRepositoryContract(port);

            var expected = isRepository
                ? ArchitectureAssemblies.DomainLayer
                : ArchitectureAssemblies.ApplicationLayer;

            string layer = isRepository ? "domain" : "application";

            expected.ShouldContain(
                port.Assembly,
                $"{port.FullName} is declared in '{port.Assembly.GetName().Name}' and must be " +
                $"declared in the {layer} layer: a contract is owned inward of the module that " +
                "satisfies it, never by the adapter itself. Repository contracts live in the domain " +
                "under Features/<Feature>/Repositories; every other port lives in the application " +
                "layer.");
        }
    }

    private static bool IsRepositoryContract(Type port) =>
        port.Namespace?.EndsWith(".Repositories", StringComparison.Ordinal) is true;

    [Fact]
    public void InfrastructureAssemblies_ExposeNonPublicTypesToReflection()
    {
        foreach (var assembly in ArchitectureAssemblies.ProductionInfrastructure)
        {
            assembly.GetTypes()
                .Where(type => type is { IsClass: true, IsPublic: false, IsNested: false })
                .ShouldNotBeEmpty(
                    $"'{assembly.GetName().Name}' appears to contain no internal classes, which " +
                    "cannot be right for a module whose adapters are all internal.");
        }
    }
}

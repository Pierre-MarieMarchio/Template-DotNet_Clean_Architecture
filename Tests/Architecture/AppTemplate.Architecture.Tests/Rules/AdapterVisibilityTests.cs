using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Application.Features.Auth.Ports.AccessTokenIssuer;
using AppTemplate.Application.Features.Auth.Ports.ConfirmationEmailComposer;
using AppTemplate.Application.Features.Auth.Ports.EmailConfirmationTokens;
using AppTemplate.Application.Features.Auth.Ports.RefreshTokenGrants;
using AppTemplate.Application.Features.Auth.Ports.UserAccounts;
using AppTemplate.Application.Features.TodoLists.Ports.TodoListQueries;
using AppTemplate.Architecture.Tests.Fixtures;
using AppTemplate.Domain.Features.TodoLists.Repositories;
using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

/// <summary>
/// The visibility of the adapters. A port is the public contract; the class behind it is not.
/// <para>
/// This is what makes a module replaceable. While <c>MailKitEmailSender</c> is internal, the only
/// thing any other assembly can name is <c>IEmailSender</c>, so swapping the implementation is a
/// change inside one project. The moment an adapter is public, a caller can reference it directly
/// and the port stops being the seam.
/// </para>
/// </summary>
public sealed class AdapterVisibilityTests
{
    /// <summary>Every port AppTemplate.Application declares for an infrastructure module to implement.</summary>
    private static readonly Type[] _applicationPorts =
    [
        typeof(IUnitOfWork),
        typeof(IDateTimeProvider),
        typeof(IEmailSender),
        typeof(ICurrentUser),
        typeof(ITodoListRepository),
        typeof(ITodoListQueries),
        typeof(IUserAccounts),
        typeof(IEmailConfirmationTokens),
        typeof(IAccessTokenIssuer),
        typeof(IRefreshTokenGrants),
        typeof(IConfirmationEmailComposer),
    ];

    [Fact]
    public void Adapters_ImplementingAnApplicationPort_AreInternalToTheirModule()
    {
        var offenders = new List<string>();
        int adaptersFound = 0;

        foreach (var assembly in ArchitectureAssemblies.ProductionInfrastructure)
        {
            RuleAssertions.RequireTypes(assembly);

            foreach (var port in _applicationPorts)
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

        // Non-vacuity: if no adapter were found at all, the rule would hold and mean nothing.
        adaptersFound.ShouldBeGreaterThanOrEqualTo(
            10,
            "Fewer adapters were found than the modules are known to contain: a unit of work, a " +
            "clock, an email sender, a repository and a query service, plus the five authentication " +
            "adapters. The discovery in this rule has stopped matching them.");

        offenders.ShouldBeEmpty(
            "An adapter is internal to its module. Only the port it implements is public, which is " +
            "what keeps replacing the implementation a change inside one project.");
    }

    /// <summary>
    /// The one deliberate exception, asserted rather than assumed. <c>AppTemplate.Infrastructure.InMemory</c>
    /// ships doubles a test has to reach — it moves the clock through <c>FixedDateTimeProvider</c>
    /// and reads delivered mail out of <c>RecordedEmails</c> — so those types are public on purpose.
    /// Stating it here means the exclusion above is a decision, not an oversight.
    /// </summary>
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

    /// <summary>
    /// Proves the visibility rule bites. The same condition is applied to the public doubles in
    /// AppTemplate.Infrastructure.InMemory, which must fail it — if it does not, <c>NotBePublic</c> is
    /// matching nothing and the rule above is decorative.
    /// </summary>
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

    /// <summary>
    /// The ports themselves must be public, or the modules could not implement them from another
    /// assembly and the rule above would be guarding a contract nobody can see.
    /// </summary>
    [Fact]
    public void ApplicationPorts_ArePublicInterfaces()
    {
        _applicationPorts.ShouldNotBeEmpty();

        foreach (var port in _applicationPorts)
        {
            port.IsInterface.ShouldBeTrue($"{port.FullName} is listed as a port but is not an interface.");
            port.IsPublic.ShouldBeTrue($"{port.FullName} is a port and must be public.");

            // A repository contract speaks only in aggregate types, so it belongs beside the aggregate
            // it loads; every other port speaks in DTOs or platform concerns and belongs in
            // Application. Either way it is declared inward of the module that satisfies it, which is
            // the point.
            var expected = IsRepositoryContract(port)
                ? ArchitectureAssemblies.Domain
                : ArchitectureAssemblies.Application;

            port.Assembly.ShouldBe(
                expected,
                $"{port.FullName} must be declared in '{expected.GetName().Name}': a contract is owned " +
                "inward of the module that satisfies it, never by the adapter itself. Repository " +
                "contracts live in AppTemplate.Domain under Features/<Feature>/Repositories; every " +
                "other port lives in AppTemplate.Application.");
        }
    }

    /// <summary>
    /// A repository contract is recognised by the folder it is declared in, so the rule follows the
    /// convention rather than a hand-kept list.
    /// </summary>
    private static bool IsRepositoryContract(Type port) =>
        port.Namespace?.EndsWith(".Repositories", StringComparison.Ordinal) is true;

    /// <summary>
    /// Guards the assumption the whole file rests on: that reflection over these assemblies can see
    /// non-public types at all. If <c>GetTypes()</c> returned only public types the visibility rule
    /// would trivially find nothing to complain about.
    /// </summary>
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

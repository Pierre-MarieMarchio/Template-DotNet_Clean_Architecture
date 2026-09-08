using AppTemplate.Architecture.Tests.Fixtures;
using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

/// <summary>
/// The dependency rule of the architecture, as assertions: the Domain knows nothing, the
/// Application layer knows only the Domain, and neither knows any technology.
/// <para>
/// These are checked against the compiled IL, not the project file, so they also catch a
/// dependency that arrives transitively — a package reference added to the Domain, or a type
/// pulled in through a <c>global using</c>.
/// </para>
/// </summary>
public sealed class LayerDependencyTests
{
    /// <summary>
    /// Everything the Domain must not know about. Three groups: the layers above it, the
    /// persistence and transport technologies, and the composition/serialisation frameworks
    /// whose attributes tend to creep onto entities.
    /// </summary>
    private static readonly string[] _forbiddenInDomain =
    [
        ArchitectureAssemblies.ApplicationNamespace,
        ArchitectureAssemblies.InfrastructureNamespace,
        ArchitectureAssemblies.PresentationNamespace,
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Microsoft.Extensions.DependencyInjection",
        "FluentValidation",
        "Npgsql",
        "MailKit",
        "System.Text.Json",
    ];

    /// <summary>
    /// What the Application layer must not know about. FluentValidation and the
    /// <c>Microsoft.Extensions.DependencyInjection</c> abstractions are deliberately absent from
    /// this list: the layer declares its own validators and owns its own registration entry point.
    /// Everything that talks to a database, a mail relay or an HTTP pipeline is not.
    /// </summary>
    private static readonly string[] _forbiddenInApplication =
    [
        ArchitectureAssemblies.InfrastructureNamespace,
        ArchitectureAssemblies.PresentationNamespace,
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Npgsql",
        "MailKit",

        // Not a layering violation: a refusal. A controller here names the use case it calls and F12
        // reaches the implementation, which a dispatcher would replace with a runtime lookup and a
        // pipeline nobody reads. Listed so the day someone adds the package the build says no.
        "MediatR",
    ];

    /// <summary>
    /// Every project of the domain layer, not one of them. The forbidden list is the same for all:
    /// a rule written against the project somebody happened to have in mind leaves the rest of the
    /// layer unread, and the primitives are the half everything else compiles against.
    /// </summary>
    [Fact]
    public void TheDomainLayer_DependsOnNothing()
    {
        foreach (var domain in ArchitectureAssemblies.DomainLayer)
        {
            RuleAssertions.RequireTypes(domain);

            Types.InAssembly(domain)
                .ShouldNot()
                .HaveDependencyOnAny(_forbiddenInDomain)
                .GetResult()
                .ShouldHold(
                    $"{ArchitectureAssemblies.NamespaceOf(domain)} belongs to the innermost layer and " +
                    "must depend on nothing: not on the layers above it, not on EF Core or ASP.NET " +
                    "Core, not on a DI container, not on a serialiser. Forbidden: " +
                    string.Join(", ", _forbiddenInDomain));
        }
    }

    /// <summary>
    /// Every project of the application layer, for the reason the domain rule above reads all of
    /// its own: the mechanisms and the features are held to one standard, and a list that named one
    /// project would leave the other unread.
    /// </summary>
    [Fact]
    public void TheApplicationLayer_DependsOnlyOnTheDomain()
    {
        foreach (var application in ArchitectureAssemblies.ApplicationLayer)
        {
            RuleAssertions.RequireTypes(application);

            Types.InAssembly(application)
                .ShouldNot()
                .HaveDependencyOnAny(_forbiddenInApplication)
                .GetResult()
                .ShouldHold(
                    $"{ArchitectureAssemblies.NamespaceOf(application)} may depend on the domain, " +
                    "FluentValidation and the DI abstractions and nothing else. A port belongs here; " +
                    "the adapter that implements it belongs in an infrastructure module. Forbidden: " +
                    string.Join(", ", _forbiddenInApplication));
        }
    }

    /// <summary>
    /// The same two rules read off the assembly manifests, which is the linker's own view: an entry
    /// appears there only if something in the assembly actually uses it.
    /// <para>
    /// Asserted in both directions on purpose. The negative rules above would all hold for a Domain
    /// nobody depends on and an Application layer that had quietly stopped using it, so the positive
    /// half is what stops them guarding an empty room.
    /// </para>
    /// </summary>
    [Fact]
    public void TheManifests_ShowTheDependencyPointingInwardAndNoFurther()
    {
        TypeFacts.ReferencesAssembly(ArchitectureAssemblies.Application, ArchitectureAssemblies.DomainNamespace)
            .ShouldBeTrue(
                "AppTemplate.Application does not name AppTemplate.Domain in its manifest, so it no longer uses the " +
                "domain model at all and every rule about the direction between them is moot.");

        TypeFacts.ReferencesAssembly(ArchitectureAssemblies.Domain, ArchitectureAssemblies.DomainCoreNamespace)
            .ShouldBeTrue(
                "AppTemplate.Domain does not name AppTemplate.Domain.Core in its manifest, so its " +
                "aggregates are no longer built from the shared primitives and the split between " +
                "the two has stopped meaning anything.");

        TypeFacts.ReferencesAssembly(ArchitectureAssemblies.DomainCore, ArchitectureAssemblies.DomainNamespace)
            .ShouldBeFalse(
                "AppTemplate.Domain.Core must not name AppTemplate.Domain in its manifest: the " +
                "primitives know no feature, which is the whole reason they are a project of their " +
                "own.");

        foreach (var domain in ArchitectureAssemblies.DomainLayer)
        {
            string domainName = ArchitectureAssemblies.NamespaceOf(domain);

            TypeFacts.ReferencesAssembly(domain, ArchitectureAssemblies.ApplicationNamespace)
                .ShouldBeFalse($"'{domainName}' must not name AppTemplate.Application in its manifest.");

            foreach (var infrastructure in ArchitectureAssemblies.AllInfrastructure)
            {
                string moduleName = ArchitectureAssemblies.NamespaceOf(infrastructure);

                TypeFacts.ReferencesAssembly(domain, moduleName)
                    .ShouldBeFalse($"'{domainName}' must not name '{moduleName}' in its manifest.");
            }
        }

        TypeFacts.ReferencesAssembly(
                ArchitectureAssemblies.Application,
                ArchitectureAssemblies.ApplicationCoreNamespace)
            .ShouldBeTrue(
                "AppTemplate.Application does not name AppTemplate.Application.Core in its " +
                "manifest, so its use cases no longer answer with the shared Result or carry the " +
                "shared paging contracts, and the split between the two has stopped meaning " +
                "anything.");

        TypeFacts.ReferencesAssembly(
                ArchitectureAssemblies.ApplicationCore,
                ArchitectureAssemblies.DomainNamespace)
            .ShouldBeFalse(
                "AppTemplate.Application.Core must not name AppTemplate.Domain in its manifest: the " +
                "mechanisms know no feature, and the two files here that need a domain type need " +
                "only the primitives.");

        TypeFacts.ReferencesAssembly(
                ArchitectureAssemblies.ApplicationCore,
                ArchitectureAssemblies.ApplicationNamespace)
            .ShouldBeFalse(
                "AppTemplate.Application.Core must not name AppTemplate.Application in its " +
                "manifest. The arrow points from the features to the mechanisms; the reverse would " +
                "make the mechanisms unusable without the example features.");

        foreach (var application in ArchitectureAssemblies.ApplicationLayer)
        {
            string applicationName = ArchitectureAssemblies.NamespaceOf(application);

            foreach (var infrastructure in ArchitectureAssemblies.AllInfrastructure)
            {
                string moduleName = ArchitectureAssemblies.NamespaceOf(infrastructure);

                TypeFacts.ReferencesAssembly(application, moduleName)
                    .ShouldBeFalse($"'{applicationName}' must not name '{moduleName}' in its manifest.");
            }
        }
    }

    /// <summary>
    /// The half of "authentication is optional" that is actually true, and the one the split bought:
    /// nothing in the business application project or in the mechanisms names anything in
    /// <c>AppTemplate.Application.Auth</c>.
    /// <para>
    /// Read off the manifests, which is the linker's own view: an entry appears there only because
    /// something in the assembly uses it. So this is not a statement about project files that could
    /// be true while a type reached across anyway — it is a statement about what the code does.
    /// </para>
    /// <para>
    /// The arrow is allowed to point the other way and does: authentication is built on the
    /// mechanisms. What must not appear is the reverse, because a business feature that named an
    /// authentication type would make the whole project mandatory again, and per-feature
    /// registration would buy nothing.
    /// <see cref="Composition.ContainerCompositionTests.RemovingAuthentication_IsHeldUpByTwoInfrastructureCouplings_NotByTheApplicationLayer"/>
    /// carries what stops a full removal being free.
    /// </para>
    /// </summary>
    [Fact]
    public void TheApplicationLayer_KnowsNothingOfAuthentication()
    {
        TypeFacts.ReferencesAssembly(
                ArchitectureAssemblies.ApplicationAuth,
                ArchitectureAssemblies.ApplicationCoreNamespace)
            .ShouldBeTrue(
                "AppTemplate.Application.Auth does not name AppTemplate.Application.Core in its " +
                "manifest, so it is no longer built on the shared mechanisms and the direction this " +
                "rule is about has stopped existing.");

        TypeFacts.ReferencesAssembly(
                ArchitectureAssemblies.Application,
                ArchitectureAssemblies.ApplicationAuthNamespace)
            .ShouldBeFalse(
                "AppTemplate.Application names AppTemplate.Application.Auth in its manifest. A " +
                "business feature that reaches into authentication makes that project mandatory in " +
                "every host again, which is exactly what registering per feature was for.");

        TypeFacts.ReferencesAssembly(
                ArchitectureAssemblies.ApplicationCore,
                ArchitectureAssemblies.ApplicationAuthNamespace)
            .ShouldBeFalse(
                "AppTemplate.Application.Core names AppTemplate.Application.Auth in its manifest. " +
                "The mechanisms are the innermost thing in this layer; anything they learned would " +
                "be learned by everything above them.");
    }

    /// <summary>
    /// Proves the machinery behind <see cref="TheDomainLayer_DependsOnNothing"/> can fail.
    /// <para>
    /// The same forbidden list is applied to <c>AppTemplate.Infrastructure.Persistence</c>, which depends on
    /// EF Core, on the DI abstractions and on AppTemplate.Application by design. If this passes, then
    /// <c>HaveDependencyOnAny</c> is not detecting anything and the rule above is worthless.
    /// </para>
    /// </summary>
    [Fact]
    public void TheDomainRule_IsSensitive_AndDetectsARealViolation()
    {
        RuleAssertions.RequireTypes(ArchitectureAssemblies.Persistence);

        Types.InAssembly(ArchitectureAssemblies.Persistence)
            .ShouldNot()
            .HaveDependencyOnAny(_forbiddenInDomain)
            .GetResult()
            .ShouldDetectAViolation(
                "AppTemplate.Infrastructure.Persistence depends on EF Core, on the DI abstractions and on " +
                "AppTemplate.Application, so applying the Domain's forbidden list to it must fail. That it " +
                "passed means NetArchTest is detecting no dependencies at all, and every " +
                "'ShouldNot().HaveDependencyOnAny(...)' rule in this project is vacuous.");
    }
}

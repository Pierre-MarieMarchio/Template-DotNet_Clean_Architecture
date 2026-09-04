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

    [Fact]
    public void Domain_DependsOnNothing()
    {
        RuleAssertions.RequireTypes(ArchitectureAssemblies.Domain);

        Types.InAssembly(ArchitectureAssemblies.Domain)
            .ShouldNot()
            .HaveDependencyOnAny(_forbiddenInDomain)
            .GetResult()
            .ShouldHold(
                "AppTemplate.Domain is the innermost layer and must depend on nothing: not on the layers " +
                "above it, not on EF Core or ASP.NET Core, not on a DI container, not on a " +
                "serialiser. Forbidden: " + string.Join(", ", _forbiddenInDomain));
    }

    [Fact]
    public void Application_DependsOnlyOnTheDomain()
    {
        RuleAssertions.RequireTypes(ArchitectureAssemblies.Application);

        Types.InAssembly(ArchitectureAssemblies.Application)
            .ShouldNot()
            .HaveDependencyOnAny(_forbiddenInApplication)
            .GetResult()
            .ShouldHold(
                "AppTemplate.Application may depend on AppTemplate.Domain, FluentValidation and the DI abstractions " +
                "and nothing else. A port belongs here; the adapter that implements it belongs in " +
                "an infrastructure module. Forbidden: " + string.Join(", ", _forbiddenInApplication));
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

        TypeFacts.ReferencesAssembly(ArchitectureAssemblies.Domain, ArchitectureAssemblies.ApplicationNamespace)
            .ShouldBeFalse("AppTemplate.Domain must not name AppTemplate.Application in its manifest.");

        foreach (var infrastructure in ArchitectureAssemblies.AllInfrastructure)
        {
            string moduleName = ArchitectureAssemblies.NamespaceOf(infrastructure);

            TypeFacts.ReferencesAssembly(ArchitectureAssemblies.Domain, moduleName)
                .ShouldBeFalse($"AppTemplate.Domain must not name '{moduleName}' in its manifest.");

            TypeFacts.ReferencesAssembly(ArchitectureAssemblies.Application, moduleName)
                .ShouldBeFalse($"AppTemplate.Application must not name '{moduleName}' in its manifest.");
        }
    }

    /// <summary>
    /// Proves the machinery behind <see cref="Domain_DependsOnNothing"/> can fail.
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

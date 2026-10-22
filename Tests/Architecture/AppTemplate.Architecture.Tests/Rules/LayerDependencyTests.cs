using AppTemplate.Architecture.Tests.Fixtures;
using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace AppTemplate.Architecture.Tests.Rules;

public sealed class LayerDependencyTests
{
    private static readonly string[] _forbiddenInDomain =
    [
        ArchitectureAssemblies.ApplicationNamespace,
        ArchitectureAssemblies.InfrastructureNamespace,
        ArchitectureAssemblies.PresentationNamespace,
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Microsoft.Extensions.DependencyInjection",
        "FluentValidation",
        //#if (postgreSql)
        "Npgsql",
        //#endif
        /*#if (sqlite)
        "Microsoft.Data.Sqlite",
        #endif*/
        "MailKit",
        "System.Text.Json",
    ];

    private static readonly string[] _forbiddenInApplication =
    [
        ArchitectureAssemblies.InfrastructureNamespace,
        ArchitectureAssemblies.PresentationNamespace,
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        //#if (postgreSql)
        "Npgsql",
        //#endif
        /*#if (sqlite)
        "Microsoft.Data.Sqlite",
        #endif*/
        "MailKit",
        "MediatR",
    ];

    [Fact]
    public void TheDomainLayer_DependsOnNothing()
    {
        foreach (var domain in ArchitectureAssemblies.DomainLayer)
        {
            /*#if (tagging)*/
            RuleAssertions.RequireTypes(domain);
            /*#else
            if (domain != ArchitectureAssemblies.Domain)
            {
                RuleAssertions.RequireTypes(domain);
            }
            #endif*/

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

    [Fact]
    public void TheManifests_ShowTheDependencyPointingInwardAndNoFurther()
    {
        //#if (tagging)
        TypeFacts.ReferencesAssembly(ArchitectureAssemblies.Application, ArchitectureAssemblies.DomainNamespace)
            .ShouldBeTrue(
                "AppTemplate.Application does not name AppTemplate.Domain in its manifest, so it no longer uses the " +
                "domain model at all and every rule about the direction between them is moot.");

        TypeFacts.ReferencesAssembly(ArchitectureAssemblies.Domain, ArchitectureAssemblies.DomainCoreNamespace)
            .ShouldBeTrue(
                "AppTemplate.Domain does not name AppTemplate.Domain.Core in its manifest, so its " +
                "aggregates are no longer built from the shared primitives and the split between " +
                "the two has stopped meaning anything.");
        //#endif

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

        //#if (anyFeature)
        TypeFacts.ReferencesAssembly(
                ArchitectureAssemblies.Application,
                ArchitectureAssemblies.ApplicationCoreNamespace)
            .ShouldBeTrue(
                "AppTemplate.Application does not name AppTemplate.Application.Core in its " +
                "manifest, so its use cases no longer answer with the shared Result or carry the " +
                "shared paging contracts, and the split between the two has stopped meaning " +
                "anything.");
        //#endif

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
    //#if (localAuth)

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
    //#endif

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

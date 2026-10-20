using System.Reflection;
using AppTemplate.Application;
//#if (localAuth)
using AppTemplate.Application.Auth;
//#endif
using AppTemplate.Application.Core;
//#if (tagging)
using AppTemplate.Domain.Common.Tagging;
//#endif
using AppTemplate.Domain.Core.Common.Primitives;
//#if (localAuth)
using AppTemplate.Infrastructure.Auth;
//#endif
using AppTemplate.Infrastructure.Email;
using AppTemplate.Infrastructure.InMemory;
using AppTemplate.Infrastructure.Persistence;
//#if (files)
using AppTemplate.Infrastructure.Storage;
//#endif
using AppTemplate.Presentation.Core;

namespace AppTemplate.Architecture.Tests.Fixtures;

internal static class ArchitectureAssemblies
{
    internal const string DomainNamespace = "AppTemplate.Domain";
    internal const string DomainCoreNamespace = "AppTemplate.Domain.Core";
    internal const string ApplicationNamespace = "AppTemplate.Application";
    internal const string ApplicationCoreNamespace = "AppTemplate.Application.Core";
    //#if (localAuth)
    internal const string ApplicationAuthNamespace = "AppTemplate.Application.Auth";
    //#endif
    internal const string InfrastructureNamespace = "AppTemplate.Infrastructure";
    internal const string PresentationNamespace = "AppTemplate.Api";
    internal const string PresentationCoreNamespace = "AppTemplate.Presentation.Core";

    internal const string PersistenceNamespace = "AppTemplate.Infrastructure.Persistence";
    //#if (localAuth)
    internal const string IdentityInfrastructureNamespace = "AppTemplate.Infrastructure.Auth";
    //#endif
    internal const string EmailInfrastructureNamespace = "AppTemplate.Infrastructure.Email";
    internal const string InMemoryInfrastructureNamespace = "AppTemplate.Infrastructure.InMemory";
    //#if (files)
    internal const string StorageInfrastructureNamespace = "AppTemplate.Infrastructure.Storage";
    //#endif

    internal const string PersistenceCommonNamespace = "AppTemplate.Infrastructure.Persistence.Common";

    internal const string PersistenceFeaturesNamespace = "AppTemplate.Infrastructure.Persistence.Features";

    internal const string DomainFeaturesNamespace = "AppTemplate.Domain.Features";

    internal const string ApplicationFeaturesNamespace = "AppTemplate.Application.Features";

    /*#if (tagging)*/
    private static readonly Lazy<Assembly> _domain = new(() => Anchor(typeof(Tag), DomainNamespace));
    /*#else
    private static readonly Lazy<Assembly> _domain = new(() => Load(DomainNamespace));
    #endif*/

    private static readonly Lazy<Assembly> _domainCore =
        new(() => Anchor(typeof(IAggregateRoot), DomainCoreNamespace));

    private static readonly Lazy<Assembly> _application =
        new(() => Anchor(typeof(ApplicationModule), ApplicationNamespace));

    private static readonly Lazy<Assembly> _applicationCore =
        new(() => Anchor(typeof(ApplicationCoreModule), ApplicationCoreNamespace));

    //#if (localAuth)
    private static readonly Lazy<Assembly> _applicationAuth =
        new(() => Anchor(typeof(ApplicationAuthModule), ApplicationAuthNamespace));

    //#endif
    private static readonly Lazy<Assembly> _presentationCore =
        new(() => Anchor(typeof(PresentationCoreModule), PresentationCoreNamespace));

    private static readonly Lazy<Assembly> _persistence =
        new(() => Anchor(typeof(PersistenceModule), PersistenceNamespace));

    //#if (localAuth)
    private static readonly Lazy<Assembly> _identityInfrastructure =
        new(() => Anchor(typeof(AuthModule), IdentityInfrastructureNamespace));

    //#endif
    private static readonly Lazy<Assembly> _emailInfrastructure =
        new(() => Anchor(typeof(EmailModule), EmailInfrastructureNamespace));

    private static readonly Lazy<Assembly> _inMemoryInfrastructure =
        new(() => Anchor(typeof(InMemoryModule), InMemoryInfrastructureNamespace));

    //#if (files)
    private static readonly Lazy<Assembly> _storageInfrastructure =
        new(() => Anchor(typeof(StorageModule), StorageInfrastructureNamespace));

    //#endif
    private static readonly Lazy<IReadOnlyList<Assembly>> _allInfrastructure =
        new(() => LayerAssemblies("Infrastructure"));

    private static readonly Lazy<IReadOnlyList<Assembly>> _productionInfrastructure =
        new(() => LayerAssemblies("Infrastructure", module => ProjectReferenceGraph.Hosts
            .Any(host => host.References.Contains(module.Name))));

    private static readonly Lazy<IReadOnlyList<Assembly>> _applicationLayer =
        new(() => LayerAssemblies("Application"));

    private static readonly Lazy<IReadOnlyList<Assembly>> _domainLayer =
        new(() => LayerAssemblies("Domain"));

    internal static Assembly Domain => _domain.Value;

    internal static Assembly DomainCore => _domainCore.Value;

    internal static Assembly Application => _application.Value;

    internal static Assembly ApplicationCore => _applicationCore.Value;

    //#if (localAuth)
    internal static Assembly ApplicationAuth => _applicationAuth.Value;

    //#endif
    internal static Assembly PresentationCore => _presentationCore.Value;

    internal static Assembly Persistence => _persistence.Value;

    //#if (localAuth)
    internal static Assembly IdentityInfrastructure => _identityInfrastructure.Value;

    //#endif
    internal static Assembly EmailInfrastructure => _emailInfrastructure.Value;

    internal static Assembly InMemoryInfrastructure => _inMemoryInfrastructure.Value;

    //#if (files)
    internal static Assembly StorageInfrastructure => _storageInfrastructure.Value;

    //#endif
    internal static IReadOnlyList<Assembly> ProductionInfrastructure => _productionInfrastructure.Value;

    internal static IReadOnlyList<Assembly> AllInfrastructure => _allInfrastructure.Value;

    internal static IReadOnlyList<Assembly> ApplicationLayer => _applicationLayer.Value;

    internal static IReadOnlyList<Assembly> DomainLayer => _domainLayer.Value;

    internal static string NamespaceOf(Assembly assembly) =>
        assembly.GetName().Name
        ?? throw new InvalidOperationException("A product assembly has no simple name.");

    private static IReadOnlyList<Assembly> LayerAssemblies(
        string layerFolder,
        Func<ProjectNode, bool>? keep = null)
    {
        var projects = ProjectReferenceGraph
            .ProjectsInLayer(layerFolder)
            .Where(keep ?? (_ => true))
            .ToList();

        if (projects.Count == 0)
        {
            throw new InvalidOperationException(
                $"The project graph reports no project under 'Src\\{layerFolder}' for this set. " +
                "Every rule written over it would pass by reading nothing.");
        }

        return [.. projects.Select(project => Load(project.Name))];
    }

    private static Assembly Load(string assemblyName)
    {
        try
        {
            return Assembly.Load(new AssemblyName(assemblyName));
        }
        catch (Exception exception) when (exception is FileNotFoundException or BadImageFormatException)
        {
            throw new InvalidOperationException(
                $"'{assemblyName}' exists under Src but could not be loaded by name. " +
                "AppTemplate.Architecture.Tests has to reference every project it addresses, or the " +
                "rules aimed at this one check nothing.",
                exception);
        }
    }

    private static Assembly Anchor(Type anchor, string expectedAssemblyName)
    {
        var assembly = anchor.Assembly;
        string? actualName = assembly.GetName().Name;

        if (!string.Equals(actualName, expectedAssemblyName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Expected '{anchor.FullName}' to live in assembly '{expectedAssemblyName}' but it " +
                "lives in '" + actualName + "'. The architecture rules address assemblies and " +
                "namespaces by name; fix the anchor before trusting them.");
        }

        var loaded = Assembly.Load(new AssemblyName(expectedAssemblyName));

        if (!ReferenceEquals(loaded, assembly))
        {
            throw new InvalidOperationException(
                $"'{expectedAssemblyName}' resolved to two different assembly instances.");
        }

        return assembly;
    }
}

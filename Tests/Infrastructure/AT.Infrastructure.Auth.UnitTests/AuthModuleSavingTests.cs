using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Core.Common.Contexts;
using AppTemplate.Infrastructure.Core.Common.Saving;
/*#if (versionCounter)
using AppTemplate.Infrastructure.Core.Common.Saving.Versioning;
#endif*/
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.UnitTests;

public sealed class AuthModuleSavingTests
{
    [Fact]
    public void AddAuthModule_AttachesNoSaveChangesInterceptorToItsContext()
    {
        using var provider = Compose();
        using var scope = provider.CreateScope();

        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<AuthDbContext>>();
        var interceptors = options.FindExtension<CoreOptionsExtension>()?.Interceptors ?? [];

        interceptors.OfType<ISaveChangesInterceptor>()
            /*#if (versionCounter)
            .Where(interceptor => interceptor is not VersionStampingSaveChangesInterceptor)
            #endif*/
            .ShouldBeEmpty();
    }

    [Fact]
    public void AddAuthModule_ResolvesItsUnitOfWork_WithoutAnAuditActor()
    {
        using var provider = Compose();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IContextUnitOfWork<AuthDbContext>>().ShouldNotBeNull();
    }

    private static ServiceProvider Compose()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ConnectionStrings:Default"] = DatabaseProvider.DesignTimeConnectionString,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        return services
            .AddAuthModule(configuration)
            .BuildServiceProvider(validateScopes: true);
    }
}

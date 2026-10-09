using AppTemplate.Infrastructure.Auth.Common.Contexts;
using AppTemplate.Infrastructure.Core.Common.Saving;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Auth.UnitTests;

/// <summary>
/// The identity context commits through its own unit of work and nothing else: none of its rows is
/// audited, and it has no aggregate to flush and no event to publish. A business interceptor attached
/// here would run on every account write, and would flush or publish the business half's pending
/// work on a commit that is not its own.
/// </summary>
/// <remarks>
/// No database: composing and reading the options opens no connection.
/// </remarks>
public sealed class AuthModuleSavingTests
{
    [Fact]
    public void AddAuthModule_AttachesNoSaveChangesInterceptorToItsContext()
    {
        using var provider = Compose();
        using var scope = provider.CreateScope();

        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<AuthDbContext>>();
        var interceptors = options.FindExtension<CoreOptionsExtension>()?.Interceptors ?? [];

        interceptors.OfType<ISaveChangesInterceptor>().ShouldBeEmpty();
    }

    /// <summary>
    /// Nothing that only a business save needs — an audit actor, a tracker, a dispatcher — has to be
    /// registered for the identity context to commit.
    /// </summary>
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
                ["ConnectionStrings:Default"] = "Host=nowhere;Database=apptemplate;Username=u;Password=p",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        return services
            .AddAuthModule(configuration)
            .BuildServiceProvider(validateScopes: true);
    }
}

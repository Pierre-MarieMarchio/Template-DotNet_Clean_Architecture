using AppTemplate.Infrastructure.Core.Common.Contexts;
using AppTemplate.Infrastructure.Core.Common.Options;
using AppTemplate.Infrastructure.Core.Common.Saving.Auditing;
using AppTemplate.Infrastructure.Core.Common.Saving.DomainEvents;
using AppTemplate.Infrastructure.Core.Common.Saving.Tracking;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests;

public sealed class PersistenceModuleInterceptorOrderTests
{
    private static readonly string[] _expectedOrder =
    [
        nameof(AggregateFlushSaveChangesInterceptor),
        nameof(AuditingSaveChangesInterceptor),
        nameof(DomainEventDispatchSaveChangesInterceptor),
        .. SaveChangesInterceptorNames(new DbContextOptionsBuilder<AppDbContext>()
            .UseDatabase(DatabaseProvider.DesignTimeConnectionString, new DatabaseOptions(), AppDbContext.Migrations)
            .Options),
    ];

    [Fact]
    public void AddPersistenceModule_AttachesTheSaveChangesInterceptorsInFlushAuditDispatchOrder()
    {
        using var provider = Compose();
        using var scope = provider.CreateScope();

        var attached = SaveChangesInterceptorNames(scope.ServiceProvider);

        attached.Count.ShouldBe(
            _expectedOrder.Length,
            $"the context should attach {_expectedOrder.Length} save-changes interceptors, and it "
            + $"attached {attached.Count}: {Readable(attached)}");

        attached.SequenceEqual(_expectedOrder, StringComparer.Ordinal).ShouldBeTrue(
            "the order of the save-changes interceptors is load-bearing. Expected "
            + $"{Readable(_expectedOrder)} but the container composed {Readable(attached)}. Flushing "
            + "must come first (it is what makes a root Modified when only a child changed), audit "
            + "stamping second (it only acts on an entry that is already Added or Modified), and event "
            + "dispatch after them (it publishes what the other two prepared), and whatever the database "
            + "provider adds comes last, once the rows to write are final.");
    }

    private static string Readable(IReadOnlyList<string> names) => string.Join(" -> ", names);

    private static List<string> SaveChangesInterceptorNames(IServiceProvider services) =>
        SaveChangesInterceptorNames(services.GetRequiredService<DbContextOptions<AppDbContext>>());

    private static List<string> SaveChangesInterceptorNames(DbContextOptions options)
    {
        var core = options.FindExtension<CoreOptionsExtension>()
            ?? throw new InvalidOperationException(
                "The composed options carry no core extension, so no interceptor could have been added.");

        return [.. (core.Interceptors ?? [])
            .OfType<ISaveChangesInterceptor>()
            .Select(interceptor => interceptor.GetType().Name)];
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
        services.AddSingleton<IAuditActor, NobodyInParticular>();

        return services
            .AddPersistenceModule(configuration)
            .BuildServiceProvider(validateScopes: true);
    }

    private sealed class NobodyInParticular : IAuditActor
    {
        public Guid? UserId => null;
    }
}

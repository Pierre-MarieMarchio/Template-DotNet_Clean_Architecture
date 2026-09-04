using AppTemplate.Application.Common.Abstractions;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using AppTemplate.Infrastructure.Persistence.Common.Saving.Auditing;
using AppTemplate.Infrastructure.Persistence.Common.Saving.DomainEvents;
using AppTemplate.Infrastructure.Persistence.Common.Saving.Tracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests;

/// <summary>
/// The three <see cref="ISaveChangesInterceptor"/>s reach the context in the order
/// <see cref="PersistenceModule"/> declares to be load-bearing: flush, then audit, then dispatch.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is read, and why that and not something else.</b> The assertion reads the composed
/// <c>DbContextOptions&lt;AppDbContext&gt;</c> — the very object the container hands to
/// <see cref="AppDbContext"/> — and takes the interceptors in the order EF stored them. That is the
/// order EF invokes them in, so it is the thing that actually matters.
/// </para>
/// <para>
/// The two nearby alternatives would both pass while the guarantee was broken. The order of the
/// <c>services.TryAddScoped&lt;…&gt;</c> calls says nothing: each interceptor is its own service, and
/// resolving three unrelated services has no order at all — only the argument list of
/// <c>AddInterceptors</c> does. And re-reading the source of <c>AddContext</c> would assert the text of
/// the module rather than the container it produces.
/// </para>
/// <para>
/// No database is touched: <c>UseNpgsql</c> builds options, and reading them opens no connection. The
/// connection string below therefore names a host that need not exist.
/// </para>
/// </remarks>
public sealed class PersistenceModuleInterceptorOrderTests
{
    /// <summary>
    /// Flush first, because it is what writes an aggregate's state onto its rows; audit second,
    /// because it only stamps an entry the flush has already made <c>Modified</c>; dispatch last,
    /// because it publishes what the other two prepared.
    /// </summary>
    private static readonly string[] _expectedOrder =
    [
        nameof(AggregateFlushSaveChangesInterceptor),
        nameof(AuditingSaveChangesInterceptor),
        nameof(DomainEventDispatchSaveChangesInterceptor),
    ];

    [Fact]
    public void AddPersistenceModule_AttachesTheSaveChangesInterceptorsInFlushAuditDispatchOrder()
    {
        using var provider = Compose();
        using var scope = provider.CreateScope();

        var attached = SaveChangesInterceptorNames(scope.ServiceProvider);

        // The floor: three of them were found. Without this the comparison below would be satisfied by
        // a context that attaches none, and the test would pass having read nothing.
        attached.Count.ShouldBe(
            _expectedOrder.Length,
            $"the context should attach {_expectedOrder.Length} save-changes interceptors, and it "
            + $"attached {attached.Count}: {Readable(attached)}");

        attached.SequenceEqual(_expectedOrder, StringComparer.Ordinal).ShouldBeTrue(
            "the order of the save-changes interceptors is load-bearing. Expected "
            + $"{Readable(_expectedOrder)} but the container composed {Readable(attached)}. Flushing "
            + "must come first (it is what makes a root Modified when only a child changed), audit "
            + "stamping second (it only acts on an entry that is already Added or Modified), and event "
            + "dispatch last (it publishes what the other two prepared).");
    }

    private static string Readable(IReadOnlyList<string> names) => string.Join(" -> ", names);

    private static List<string> SaveChangesInterceptorNames(IServiceProvider services)
    {
        var options = services.GetRequiredService<DbContextOptions<AppDbContext>>();

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
                ["ConnectionStrings:Default"] = "Host=nowhere;Database=apptemplate;Username=u;Password=p",
            })
            .Build();

        var services = new ServiceCollection();

        // The two dependencies the interceptors take from outside this module: the caller the audit
        // columns are stamped with, and a logger for the dispatcher. The host supplies both.
        services.AddLogging();
        services.AddSingleton<ICurrentUser, NobodyInParticular>();

        return services
            .AddPersistenceModule(configuration)
            .BuildServiceProvider(validateScopes: true);
    }

    private sealed class NobodyInParticular : ICurrentUser
    {
        public Guid? UserId => null;
    }
}

using AppTemplate.Application.Features.Files.Ports.OwnerQuotaLock;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using AppTemplate.Infrastructure.Persistence.Features.Files.Adapters.Locks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.Files.Adapters.Locks;

public sealed class OwnerQuotaLockTests
{
    private const string _database = "Data Source=quota-lock-tests.db";

    private static readonly UserId _owner = UserId.Create(Guid.Parse("01890a5d-ac96-774b-bcce-b302099a8057"));
    private static readonly FileCategory _document = FileCategory.Create("document");
    private static readonly TimeSpan _shortWait = TimeSpan.FromMilliseconds(200);

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ASecondHolderOfTheSameOwnerAndCategory_WaitsItsLimit_ThenTimesOut()
    {
        var holder = Lock(_database);
        var waiter = Lock(_database);
        var released = new TaskCompletionSource();

        var holding = holder.RunExclusivelyAsync(_owner, _document, _ => HoldUntilAsync(released.Task), TestToken);

        await Should.ThrowAsync<OwnerQuotaLockTimeoutException>(() =>
            waiter.RunExclusivelyAsync(_owner, _document, _ => Task.FromResult(true), TestToken));

        released.SetResult();
        await holding;

        (await waiter.RunExclusivelyAsync(_owner, _document, _ => Task.FromResult(true), TestToken)).ShouldBeTrue(
            "the holder released the lock, so the next caller gets it at once.");
    }

    [Fact]
    public async Task AnotherCategoryOwnerOrDatabase_DoesNotContend()
    {
        var holder = Lock(_database);
        var released = new TaskCompletionSource();

        var holding = holder.RunExclusivelyAsync(_owner, _document, _ => HoldUntilAsync(released.Task), TestToken);

        (await Lock(_database).RunExclusivelyAsync(_owner, FileCategory.Create("image"), _ => Task.FromResult(true), TestToken))
            .ShouldBeTrue();
        (await Lock(_database).RunExclusivelyAsync(UserId.Create(Guid.CreateVersion7()), _document, _ => Task.FromResult(true), TestToken))
            .ShouldBeTrue();
        (await Lock("Data Source=another-database.db").RunExclusivelyAsync(_owner, _document, _ => Task.FromResult(true), TestToken))
            .ShouldBeTrue();

        released.SetResult();
        await holding;
    }

    [Fact]
    public void ThePersistenceModule_RegistersOneLockForTheWholeProcess()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["ConnectionStrings:Default"] = _database,
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistenceModule(configuration);

        using var provider = services.BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<IOwnerQuotaLock>()
            .ShouldBeOfType<OwnerQuotaLock>()
            .ShouldBeSameAs(provider.GetRequiredService<IOwnerQuotaLock>());
    }

    [Fact]
    public void TheWaitLimit_IsTenSeconds() =>
        OwnerQuotaLock.WaitLimit.ShouldBe(TimeSpan.FromSeconds(10));

    private static async Task<bool> HoldUntilAsync(Task released)
    {
        await released;

        return true;
    }

    private static OwnerQuotaLock Lock(string connectionString) =>
        new(connectionString, NullLogger<OwnerQuotaLock>.Instance, _shortWait);
}

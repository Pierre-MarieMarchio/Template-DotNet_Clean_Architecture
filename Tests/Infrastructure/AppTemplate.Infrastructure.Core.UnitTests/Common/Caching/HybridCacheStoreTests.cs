using AppTemplate.Application.Core.Common.Ports;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Core.UnitTests.Common.Caching;

/// <summary>
/// The three things a caller depends on: a second read is served without running the factory, a
/// removal makes the next read run it again, and concurrent misses on one key run it once.
/// </summary>
public sealed class HybridCacheStoreTests
{
    private static readonly TimeSpan _lifetime = TimeSpan.FromMinutes(5);

    private readonly ServiceProvider _provider =
        new ServiceCollection().AddCacheStore().BuildServiceProvider();

    private ICacheStore Store => _provider.GetRequiredService<ICacheStore>();

    [Fact]
    public async Task ASecondRead_IsServedWithoutRunningTheFactory()
    {
        int factoryRuns = 0;
        string key = NewKey();

        string first = await Store.GetOrCreateAsync(key, Factory, _lifetime, TestToken);
        string second = await Store.GetOrCreateAsync(key, Factory, _lifetime, TestToken);

        first.ShouldBe("value 1");
        second.ShouldBe("value 1");
        factoryRuns.ShouldBe(1);

        Task<string> Factory(CancellationToken _) => Task.FromResult($"value {++factoryRuns}");
    }

    [Fact]
    public async Task RemovingAnEntry_MakesTheNextReadRunTheFactoryAgain()
    {
        int factoryRuns = 0;
        string key = NewKey();

        await Store.GetOrCreateAsync(key, Factory, _lifetime, TestToken);
        await Store.RemoveAsync(key, TestToken);
        string again = await Store.GetOrCreateAsync(key, Factory, _lifetime, TestToken);

        again.ShouldBe("value 2");
        factoryRuns.ShouldBe(2);

        Task<string> Factory(CancellationToken _) => Task.FromResult($"value {++factoryRuns}");
    }

    /// <summary>
    /// The stampede this exists to prevent: twenty callers arriving together on a cold key must not
    /// become twenty database reads.
    /// </summary>
    [Fact]
    public async Task ConcurrentMissesOnOneKey_RunTheFactoryOnce()
    {
        int factoryRuns = 0;
        string key = NewKey();

        var reads = Enumerable.Range(0, 20)
            .Select(_ => Store.GetOrCreateAsync(key, Factory, _lifetime, TestToken))
            .ToArray();

        string[] values = await Task.WhenAll(reads);

        values.Distinct(StringComparer.Ordinal).Count().ShouldBe(1);
        factoryRuns.ShouldBe(1);

        async Task<string> Factory(CancellationToken token)
        {
            Interlocked.Increment(ref factoryRuns);
            await Task.Delay(TimeSpan.FromMilliseconds(20), token);

            return "one answer";
        }
    }

    [Fact]
    public async Task ItRefusesANullFactory() =>
        await Should.ThrowAsync<ArgumentNullException>(
            () => Store.GetOrCreateAsync<string>(NewKey(), null!, _lifetime, TestToken));

    /// <summary>
    /// A key per test: the store is a real one, and two tests sharing a key would share an answer.
    /// </summary>
    private static string NewKey() => $"test:{Guid.CreateVersion7()}";

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;
}

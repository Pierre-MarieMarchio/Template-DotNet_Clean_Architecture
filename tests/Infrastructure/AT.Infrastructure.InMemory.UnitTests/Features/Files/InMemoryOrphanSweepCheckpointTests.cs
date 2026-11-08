using AppTemplate.Application.Features.Files.Ports.OrphanSweepCheckpoint;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.InMemory.UnitTests.Features.Files;

public sealed class InMemoryOrphanSweepCheckpointTests
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ANewCheckpoint_StartsTheWalkFromTheBeginning() =>
        (await Compose().GetAsync(TestToken)).ShouldBeNull();

    [Fact]
    public async Task ASavedKey_IsReadBackAcrossScopes()
    {
        using var provider = Provider();

        await using (var first = provider.CreateAsyncScope())
        {
            await first.ServiceProvider.GetRequiredService<IOrphanSweepCheckpoint>()
                .SaveAsync("t0/202608/a", TestToken);
        }

        await using var second = provider.CreateAsyncScope();
        (await second.ServiceProvider.GetRequiredService<IOrphanSweepCheckpoint>().GetAsync(TestToken))
            .ShouldBe("t0/202608/a");
    }

    [Fact]
    public async Task SavingNull_ResetsTheWalk()
    {
        var checkpoint = Compose();
        await checkpoint.SaveAsync("t0/202608/a", TestToken);

        await checkpoint.SaveAsync(null, TestToken);

        (await checkpoint.GetAsync(TestToken)).ShouldBeNull();
    }

    [Fact]
    public void AddInMemoryOrphanSweepCheckpoint_ReplacesACheckpointAlreadyRegistered()
    {
        var services = new ServiceCollection();
        var real = Substitute.For<IOrphanSweepCheckpoint>();
        services.AddSingleton(real);

        services.AddInMemoryOrphanSweepCheckpoint();

        using var provider = services.BuildServiceProvider(validateScopes: true);
        provider.GetServices<IOrphanSweepCheckpoint>().ShouldHaveSingleItem().ShouldNotBeSameAs(real);
    }

    private static ServiceProvider Provider() =>
        new ServiceCollection()
            .AddInMemoryOrphanSweepCheckpoint()
            .BuildServiceProvider(validateScopes: true);

    private static IOrphanSweepCheckpoint Compose() =>
        Provider().GetRequiredService<IOrphanSweepCheckpoint>();
}

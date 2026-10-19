using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.Files.Ports.OrphanSweepCheckpoint;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using AppTemplate.Domain.Features.Files.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Files;

/// <summary>
/// What the background passes read and write in PostgreSQL: the inspection queue's due filter and
/// order, and the orphan sweep's checkpoint row.
/// </summary>
public sealed class BackgroundPassStorageTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private const long _size = 4_096;

    private static readonly string _checksum = new('a', Sha256Checksum.Length);

    /// <summary>
    /// Due files only, in the order they became due: a deferred file queues by its next attempt, a
    /// fresh one by its registration, and one deferred past now is not offered.
    /// </summary>
    [Fact]
    public async Task TheInspectionQueue_OffersDueFilesInTheOrderTheyBecameDue()
    {
        var now = Clock.UtcNow;
        var owner = UserId.Create(Guid.CreateVersion7());

        var fresh = await StoreAsync(owner, now.AddMinutes(-3), Confirm);
        var deferredAndDue = await StoreAsync(owner, now.AddMinutes(-10), file =>
        {
            Confirm(file);
            file.DeferInspection(now.AddMinutes(-2));
        });
        var deferredAndNotDue = await StoreAsync(owner, now.AddMinutes(-20), file =>
        {
            Confirm(file);
            file.DeferInspection(now);
        });
        var available = await StoreAsync(owner, now.AddMinutes(-30), file =>
        {
            Confirm(file);
            file.MakeAvailable(now.AddMinutes(-29));
        });

        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var due = await scope.ServiceProvider.GetRequiredService<IStoredFileRepository>()
            .GetDepositedAsync(now, 100, TestToken);

        due.Select(file => file.Id).ShouldBe([fresh.Id, deferredAndDue.Id]);
        due.ShouldNotContain(file => file.Id == deferredAndNotDue.Id || file.Id == available.Id);
    }

    [Fact]
    public async Task ADeferral_SurvivesTheRoundTrip()
    {
        var now = Clock.UtcNow;
        var stored = await StoreAsync(UserId.Create(Guid.CreateVersion7()), now, file =>
        {
            Confirm(file);
            file.DeferInspection(now);
            file.DeferInspection(now.AddMinutes(1));
        });

        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var loaded = await scope.ServiceProvider.GetRequiredService<IStoredFileRepository>()
            .GetAsync(stored.Id, TestToken);

        loaded!.InspectionAttempts.ShouldBe(2);
        loaded.NextInspectionAt.ShouldBe(now.AddMinutes(3));
    }

    /// <summary>Insert, update and reset of the one checkpoint row, each read back from a fresh scope.</summary>
    [Fact]
    public async Task TheCheckpoint_IsSavedUpdatedAndReset()
    {
        (await CheckpointAsync(checkpoint => checkpoint.GetAsync(TestToken))).ShouldBeNull();

        await CheckpointAsync(checkpoint => Saved(checkpoint, "t0/202608/a"));
        (await CheckpointAsync(checkpoint => checkpoint.GetAsync(TestToken))).ShouldBe("t0/202608/a");

        await CheckpointAsync(checkpoint => Saved(checkpoint, "t0/202608/b"));
        (await CheckpointAsync(checkpoint => checkpoint.GetAsync(TestToken))).ShouldBe("t0/202608/b");

        await CheckpointAsync(checkpoint => Saved(checkpoint, null));
        (await CheckpointAsync(checkpoint => checkpoint.GetAsync(TestToken))).ShouldBeNull();
    }

    private static async Task<string?> Saved(IOrphanSweepCheckpoint checkpoint, string? lastKey)
    {
        await checkpoint.SaveAsync(lastKey, TestToken);

        return lastKey;
    }

    private async Task<string?> CheckpointAsync(Func<IOrphanSweepCheckpoint, Task<string?>> use)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();

        return await use(scope.ServiceProvider.GetRequiredService<IOrphanSweepCheckpoint>());
    }

    private async Task<StoredFile> StoreAsync(UserId owner, DateTimeOffset registeredAt, Action<StoredFile> advance)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();

        var file = StoredFile.Register(
            owner,
            FileCategory.Create("document"),
            StoredFileName.Create("report.pdf"),
            DeclaredMediaType.Create("application/pdf"),
            FileSize.Create(_size),
            Sha256Checksum.Create(_checksum),
            registeredAt);

        advance(file);

        scope.ServiceProvider.GetRequiredService<IStoredFileRepository>().Add(file);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestToken);

        return file;
    }

    private static void Confirm(StoredFile file) =>
        file.ConfirmDeposit(FileSize.Create(_size), Sha256Checksum.Create(_checksum));
}

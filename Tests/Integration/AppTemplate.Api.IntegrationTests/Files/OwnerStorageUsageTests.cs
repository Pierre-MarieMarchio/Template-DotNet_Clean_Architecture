using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Application.Common.Ports;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;
using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Domain.Features.Files.Repositories;
using AppTemplate.Domain.Features.Files.ValueObjects;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Files;

/// <summary>
/// What one owner's quota is measured against, per stored-file state, against a real database.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every state that means bytes are on the store has to count, and <c>StoredFileState</c> has
/// four of them.</b> A query that reads <c>Available</c> and <c>Pending</c> alone misses
/// <c>Deposited</c>, waiting for a verdict, and <c>Quarantined</c>, refused and kept — which makes
/// failing a file's own content inspection the cheapest way to hold bytes for ever, since nothing
/// moves a file out of <c>Quarantined</c> and the orphan sweep will not reclaim its object while a
/// row still names it.
/// </para>
/// <para>
/// Driven through the repository and the aggregate's own transitions rather than through HTTP: the
/// subject is a <c>GROUP BY</c> over a column, the states reached here are ones no single request can
/// walk a file through, and the aggregate is what decides which instants a state is allowed to carry.
/// No SQL either — a hand-written INSERT would assert against a schema of the test's own imagination.
/// </para>
/// </remarks>
public sealed class OwnerStorageUsageTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private const long _size = 4_096;

    [Fact]
    public async Task AQuarantinedFile_StillWeighsOnItsOwnersQuota()
    {
        var owner = Guid.CreateVersion7();

        await StoreAsync(owner, file =>
        {
            Confirm(file);
            file.Quarantine(Fixture.Clock.UtcNow);
        });

        var usage = await UsageForAsync(owner);

        usage.StoredBytes.ShouldBe(
            _size,
            "a quarantined file's bytes are on the store and nothing ever removes them, so an owner " +
            "whose uploads are refused would otherwise hold storage that counts against no allowance.");

        usage.StoredCount.ShouldBe(1);
        usage.TotalCount.ShouldBe(1, "the row exists, and TotalCount says it counts every row.");
        usage.CommittedBytes.ShouldBe(_size);
    }

    [Fact]
    public async Task ADepositedFileAwaitingAVerdict_AlreadyWeighsOnTheQuota()
    {
        var owner = Guid.CreateVersion7();

        await StoreAsync(owner, Confirm);

        var usage = await UsageForAsync(owner);

        usage.StoredBytes.ShouldBe(
            _size,
            "the deposit has happened; only the verdict has not. An inspection backlog must not read " +
            "as free storage, or a scanner that is down becomes a way past the allowance.");

        usage.PendingCount.ShouldBe(
            0,
            "a deposited file is no longer a registration waiting to be deposited against, and the " +
            "pending count is the anti-abuse bound on outstanding write grants.");
    }

    /// <summary>
    /// The half that was already right, asserted here so that a change moving every state into one
    /// bucket cannot pass: a registration is bytes promised, not bytes held.
    /// </summary>
    [Fact]
    public async Task APendingRegistration_CountsAsPromisedRatherThanStored()
    {
        var owner = Guid.CreateVersion7();

        await StoreAsync(owner, _ => { });

        var usage = await UsageForAsync(owner);

        usage.PendingCount.ShouldBe(1);
        usage.PendingDeclaredBytes.ShouldBe(_size);
        usage.StoredCount.ShouldBe(0, "nothing has been deposited, so nothing is on the store.");
        usage.StoredBytes.ShouldBe(0);
        usage.CommittedBytes.ShouldBe(_size, "promised bytes are what the quota refuses to ignore.");
    }

    /// <summary>
    /// Registers one file for <paramref name="owner"/> and applies <paramref name="advance"/> to it
    /// before the single commit, so the row lands in whatever state that leaves it in.
    /// </summary>
    private async Task StoreAsync(Guid owner, Action<StoredFile> advance)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();

        var file = StoredFile.Register(
            owner,
            StoredFileName.Create("quarterly-report.png"),
            DeclaredMediaType.Create("image/png"),
            FileSize.Create(_size),
            Sha256Checksum.Create(new string('a', Sha256Checksum.Length)),
            Fixture.Clock.UtcNow);

        advance(file);

        scope.ServiceProvider.GetRequiredService<IStoredFileRepository>().Add(file);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestToken);
    }

    /// <summary>
    /// The deposit the store would have reported: the declared size and digest, because a deposit of
    /// any other length or content is refused by the store rather than confirmed here.
    /// </summary>
    private static void Confirm(StoredFile file) =>
        file.ConfirmDeposit(FileSize.Create(_size), Sha256Checksum.Create(new string('a', Sha256Checksum.Length)));

    private async Task<OwnerStorageUsage> UsageForAsync(Guid owner)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();

        return await scope.ServiceProvider
            .GetRequiredService<IStoredFileQueries>()
            .GetUsageForOwnerAsync(owner, TestToken);
    }
}

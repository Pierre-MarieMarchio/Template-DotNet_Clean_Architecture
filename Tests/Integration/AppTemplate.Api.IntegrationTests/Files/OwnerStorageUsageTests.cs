using AppTemplate.Api.IntegrationTests.Infrastructure;
using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using AppTemplate.Domain.Features.Files.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Files;

public sealed class OwnerStorageUsageTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private const long _size = 4_096;

    private static readonly FileCategory _document = FileCategory.Create("document");
    private static readonly FileCategory _image = FileCategory.Create("image");

    [Fact]
    public async Task AQuarantinedFile_StillWeighsOnItsOwnersQuota()
    {
        var owner = UserId.Create(Guid.CreateVersion7());

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
        var owner = UserId.Create(Guid.CreateVersion7());

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

    [Fact]
    public async Task APendingRegistration_CountsAsPromisedRatherThanStored()
    {
        var owner = UserId.Create(Guid.CreateVersion7());

        await StoreAsync(owner, _ => { });

        var usage = await UsageForAsync(owner);

        usage.PendingCount.ShouldBe(1);
        usage.PendingDeclaredBytes.ShouldBe(_size);
        usage.StoredCount.ShouldBe(0, "nothing has been deposited, so nothing is on the store.");
        usage.StoredBytes.ShouldBe(0);
        usage.CommittedBytes.ShouldBe(_size, "promised bytes are what the quota refuses to ignore.");
    }

    [Fact]
    public async Task AFileInAnotherCategory_DoesNotWeighOnThisOne()
    {
        var owner = UserId.Create(Guid.CreateVersion7());

        await StoreAsync(owner, Confirm, _image);

        var document = await UsageForAsync(owner, _document);
        var image = await UsageForAsync(owner, _image);

        document.TotalCount.ShouldBe(0);
        document.CommittedBytes.ShouldBe(0);
        image.StoredCount.ShouldBe(1);
        image.StoredBytes.ShouldBe(_size);
    }

    private async Task StoreAsync(UserId owner, Action<StoredFile> advance, FileCategory? category = null)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();

        var file = StoredFile.Register(
            owner,
            category ?? _document,
            StoredFileName.Create("quarterly-report.png"),
            DeclaredMediaType.Create("image/png"),
            FileSize.Create(_size),
            Sha256Checksum.Create(new string('a', Sha256Checksum.Length)),
            Fixture.Clock.UtcNow);

        advance(file);

        scope.ServiceProvider.GetRequiredService<IStoredFileRepository>().Add(file);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(TestToken);
    }

    private static void Confirm(StoredFile file) =>
        file.ConfirmDeposit(FileSize.Create(_size), Sha256Checksum.Create(new string('a', Sha256Checksum.Length)));

    private async Task<OwnerStorageUsage> UsageForAsync(UserId owner, FileCategory? category = null)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();

        return await scope.ServiceProvider
            .GetRequiredService<IStoredFileQueries>()
            .GetUsageForOwnerAsync(owner, category ?? _document, TestToken);
    }
}

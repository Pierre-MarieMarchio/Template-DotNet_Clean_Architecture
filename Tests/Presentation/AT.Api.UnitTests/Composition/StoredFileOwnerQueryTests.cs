using AppTemplate.Application.Core.Common.Ports;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using AppTemplate.Domain.Features.Files.Repositories;
using AppTemplate.Infrastructure.Persistence.Common.Contexts;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Composition;

public sealed class StoredFileOwnerQueryTests
{
    private const string _checksum = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    private static readonly UserId _owner = UserId.Create(Guid.CreateVersion7());
    private static readonly UserId _stranger = UserId.Create(Guid.CreateVersion7());
    private static readonly FileCategory _open = FileCategory.Create("image");
    private static readonly FileCategory _private = FileCategory.Create("document");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheOwnersRead_LoadsTheFile()
    {
        await using var host = await SqliteSavingHost.StartAsync(Token);
        var fileId = await AFileOfTheOwnerAsync(host, _private, available: true);

        await using var scope = host.RequestScope(_owner.Value);

        (await Files(scope).GetAsync(_owner, fileId, Token)).ShouldNotBeNull().Id.ShouldBe(fileId);
    }

    [Fact]
    public async Task AStrangersRead_FindsNothing_AndBringsNoRowIntoTheContext()
    {
        await using var host = await SqliteSavingHost.StartAsync(Token);
        var fileId = await AFileOfTheOwnerAsync(host, _open, available: true);

        await using var scope = host.RequestScope(_stranger.Value);

        (await Files(scope).GetAsync(_stranger, fileId, Token)).ShouldBeNull(
            "a file open to every signed-in caller is still not theirs to change");
        Context(scope).ChangeTracker.Entries().ShouldBeEmpty();
    }

    [Fact]
    public async Task TheOwner_ReadsTheirOwnPendingFileInAPrivateCategory()
    {
        await using var host = await SqliteSavingHost.StartAsync(Token);
        var fileId = await AFileOfTheOwnerAsync(host, _private, available: false);

        await using var scope = host.RequestScope(_owner.Value);

        (await Files(scope).GetReadableAsync(_owner, fileId, [], Token)).ShouldNotBeNull();
    }

    [Fact]
    public async Task AStranger_ReadsAnAvailableFileInAnOpenCategory()
    {
        await using var host = await SqliteSavingHost.StartAsync(Token);
        var fileId = await AFileOfTheOwnerAsync(host, _open, available: true);

        await using var scope = host.RequestScope(_stranger.Value);

        (await Files(scope).GetReadableAsync(_stranger, fileId, [_open], Token)).ShouldNotBeNull();
    }

    [Fact]
    public async Task AStranger_DoesNotReadAFileOfAnOpenCategoryThatIsNotAvailable()
    {
        await using var host = await SqliteSavingHost.StartAsync(Token);
        var fileId = await AFileOfTheOwnerAsync(host, _open, available: false);

        await using var scope = host.RequestScope(_stranger.Value);

        (await Files(scope).GetReadableAsync(_stranger, fileId, [_open], Token)).ShouldBeNull();
        Context(scope).ChangeTracker.Entries().ShouldBeEmpty();
    }

    [Fact]
    public async Task AStranger_DoesNotReadAnAvailableFileOfACategoryThatIsNotOpen()
    {
        await using var host = await SqliteSavingHost.StartAsync(Token);
        var fileId = await AFileOfTheOwnerAsync(host, _private, available: true);

        await using var scope = host.RequestScope(_stranger.Value);

        (await Files(scope).GetReadableAsync(_stranger, fileId, [_open], Token)).ShouldBeNull();
        Context(scope).ChangeTracker.Entries().ShouldBeEmpty();
    }

    private static IStoredFileRepository Files(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IStoredFileRepository>();

    private static AppDbContext Context(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<AppDbContext>();

    private static async Task<Guid> AFileOfTheOwnerAsync(SqliteSavingHost host, FileCategory category, bool available)
    {
        await using var scope = host.RequestScope(_owner.Value);
        var size = FileSize.Create(1_024);
        var checksum = Sha256Checksum.Create(_checksum);
        var file = StoredFile.Register(
            _owner,
            category,
            StoredFileDeclaration.Create(
                StoredFileName.Create("holiday.png"),
                DeclaredMediaType.Create("image/png"),
                size,
                checksum),
            DateTimeOffset.UtcNow);

        if (available)
        {
            file.ConfirmDeposit(size, checksum);
            file.MakeAvailable(DateTimeOffset.UtcNow);
        }

        Files(scope).Add(file);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(Token);

        return file.Id;
    }
}

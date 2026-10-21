using AppTemplate.Application.Features.Files.Ports.FileContentInventory;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Infrastructure.Storage.Features.Files.Access.Stores;
using AppTemplate.Infrastructure.Storage.Features.Files.Adapters.Inventories;
using AppTemplate.Infrastructure.Storage.UnitTests.Features.Files.Contracts;
using AppTemplate.Infrastructure.Storage.UnitTests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Storage.UnitTests.Features.Files.Adapters.Inventories;

public sealed class FileSystemFileContentInventoryTests : FileContentInventoryContract, IDisposable
{
    private readonly TemporaryRoot _root = new();

    protected override IFileContentStore Store => new FileSystemFileContentStore(_root.Options);

    protected override IFileContentInventory Inventory => new FileSystemFileContentInventory(_root.Options);

    public void Dispose() => _root.Dispose();

    [Fact]
    public async Task ListKeysAsync_SkipsTemporaryFilesOfWritesInFlight()
    {
        Create("t0/202608/a", "t0/202608/.a.0123.tmp");

        var page = await FileSystemInventory().ListKeysAsync("t0/", null, 10, TestToken);

        page.Items.ShouldBe(["t0/202608/a"]);
    }

    [Fact]
    public async Task ListKeysAsync_DeletesTemporaryFilesACrashedWriteLeftBehind()
    {
        Create("t0/202608/a", "t0/202608/.b.stale.tmp", "t0/202609/.c.fresh.tmp");
        string stale = Path.Combine(_root.Path, "t0/202608/.b.stale.tmp");
        string fresh = Path.Combine(_root.Path, "t0/202609/.c.fresh.tmp");
        File.SetLastWriteTimeUtc(
            stale,
            DateTime.UtcNow - FileSystemFileContentInventory.StaleTemporaryFileAge - TimeSpan.FromMinutes(1));

        var page = await FileSystemInventory().ListKeysAsync("t0/", null, 10, TestToken);

        page.Items.ShouldBe(["t0/202608/a"]);
        File.Exists(stale).ShouldBeFalse();
        File.Exists(fresh).ShouldBeTrue();
    }

    private FileSystemFileContentInventory FileSystemInventory() => new(_root.Options);

    private void Create(params string[] keys)
    {
        foreach (string key in keys)
        {
            string path = Path.Combine(_root.Path, key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, [1]);
        }
    }
}

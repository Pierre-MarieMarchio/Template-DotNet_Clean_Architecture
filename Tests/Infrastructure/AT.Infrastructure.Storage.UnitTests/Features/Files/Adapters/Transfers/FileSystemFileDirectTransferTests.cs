using AppTemplate.Infrastructure.Storage.Features.Files.Adapters.Transfers;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Storage.UnitTests.Features.Files.Adapters.Transfers;

public sealed class FileSystemFileDirectTransferTests
{
    [Fact]
    public void IsSupported_IsFalse() => new FileSystemFileDirectTransfer().IsSupported.ShouldBeFalse();

    [Fact]
    public async Task AskingForAGrant_IsNotSupported()
    {
        var transfer = new FileSystemFileDirectTransfer();

        await Should.ThrowAsync<NotSupportedException>(() => transfer.CreateUploadGrantAsync(
            "t0/202608/a",
            "application/pdf",
            1,
            new string('0', 64),
            TimeSpan.FromMinutes(5),
            TestContext.Current.CancellationToken));
        await Should.ThrowAsync<NotSupportedException>(() => transfer.CreateDownloadGrantAsync(
            "t0/202608/a",
            "a.pdf",
            "application/pdf",
            TimeSpan.FromMinutes(5),
            TestContext.Current.CancellationToken));
    }
}

using System.Net;
using System.Security.Cryptography;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using AppTemplate.Infrastructure.Storage.UnitTests.Features.Files.Contracts;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Storage;

[Collection(ObjectStoreCollectionDefinition.Name)]
public sealed class StoredObjectTests(ObjectStoreFixture fixture) : FileContentStoreContract
{
    private const string _mediaType = "application/octet-stream";

    private static readonly byte[] _payload =
        "Forty-two bytes of nothing much, and a digest."u8.ToArray();

    protected override IFileContentStore Store => fixture.Content;

    protected override string NewKey() => ObjectStoreFixture.KeyUnder(ObjectStoreFixture.UniquePrefix("contract"));

    [Fact]
    public async Task Describing_AnObjectDepositedUnderTheGrantThisModuleMints_ReportsItsRealDigest()
    {
        string objectKey = ObjectStoreFixture.KeyUnder(ObjectStoreFixture.UniquePrefix("described"));

        await DepositAsync(objectKey);

        var description = await fixture.Content.DescribeAsync(objectKey, TestToken);

        description.ShouldNotBeNull();

        description.SizeInBytes.ShouldBe(_payload.Length);

        description.Checksum.ShouldBe(
            Convert.ToHexStringLower(SHA256.HashData(_payload)),
            "the digest is the store's own, computed as it wrote the bytes. Confirmation compares it " +
            "against what the client declared, so an adapter that could not obtain one refused every " +
            "upload that had actually arrived.");
    }

    [Fact]
    public async Task AWriteWhoseDigestDisagrees_IsRefusedAndLeavesNothing()
    {
        string objectKey = ObjectStoreFixture.KeyUnder(ObjectStoreFixture.UniquePrefix("mismatch"));

        var outcome = await fixture.Content.WriteAsync(
            objectKey,
            new NonSeekableStream(_payload),
            _mediaType,
            _payload.Length,
            Convert.ToHexStringLower(SHA256.HashData([1, 2, 3])),
            TestToken);

        outcome.ShouldBe(ContentWriteOutcome.ContentMismatch);
        (await fixture.Content.DescribeAsync(objectKey, TestToken)).ShouldBeNull();
    }

    [Fact]
    public async Task ASecondDepositOfDifferentBytes_IsRefusedByTheStore()
    {
        string objectKey = ObjectStoreFixture.KeyUnder(ObjectStoreFixture.UniquePrefix("swap"));

        var grant = await fixture.Transfer.CreateUploadGrantAsync(
            objectKey,
            _mediaType,
            _payload.Length,
            Convert.ToHexStringLower(SHA256.HashData(_payload)),
            TimeSpan.FromMinutes(10),
            TestToken);

        using var honest = await fixture.DepositAsync(grant, _payload, TestToken);

        honest.IsSuccessStatusCode.ShouldBeTrue(
            $"a precondition rather than the assertion; the store answered {(int)honest.StatusCode}.");

        byte[] swapped = [.. Enumerable.Repeat((byte)'Z', _payload.Length)];

        using var swap = await fixture.DepositAsync(grant, swapped, TestToken);

        swap.IsSuccessStatusCode.ShouldBeFalse(
            $"the store answered {(int)swap.StatusCode} to bytes whose digest is not the one the " +
            "grant was signed for. Accepting them would let a holder replace the content of a file " +
            $"that has already been inspected and released: " +
            await swap.Content.ReadAsStringAsync(TestToken));

        var description = await fixture.Content.DescribeAsync(objectKey, TestToken);

        description.ShouldNotBeNull();
        description.Checksum.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(_payload)));
    }

    [Fact]
    public async Task Describing_ReportsTheDigestInTheEncodingThePortAsksFor()
    {
        string objectKey = ObjectStoreFixture.KeyUnder(ObjectStoreFixture.UniquePrefix("with-digest"));

        byte[] digest = SHA256.HashData(_payload);

        await DepositAsync(objectKey);

        var description = await fixture.Content.DescribeAsync(objectKey, TestToken);

        description.ShouldNotBeNull();

        description.SizeInBytes.ShouldBe(
            _payload.Length,
            "the length is the store's own measurement of what arrived, and confirmation refuses a " +
            "file whose stored length differs from the declared one.");

        description.Checksum.ShouldBe(
            Convert.ToHexStringLower(digest),
            "the store reports base64 and the port asks for lower-case hexadecimal. They are the " +
            "same thirty-two bytes written differently, and comparing the two encodings as strings " +
            "would fail every confirmation ever attempted.");
    }

    [Fact]
    public async Task Deleting_MakesTheObjectUnreachable()
    {
        string objectKey = ObjectStoreFixture.KeyUnder(ObjectStoreFixture.UniquePrefix("deleted"));

        await DepositAsync(objectKey);

        var before = await fixture.Content.DescribeAsync(objectKey, TestToken);

        before.ShouldNotBeNull("the deletion below would prove nothing about an object never stored.");

        await fixture.Content.DeleteAsync(objectKey, TestToken);

        var after = await fixture.Content.DescribeAsync(objectKey, TestToken);

        after.ShouldBeNull(
            "reclaiming storage is the whole reason this operation exists. An object the store " +
            "still holds after a delete is paid for for ever, and nothing in this application " +
            "names it any more.");

        var grant = await fixture.Transfer.CreateDownloadGrantAsync(
            objectKey,
            "gone.bin",
            _mediaType,
            TimeSpan.FromMinutes(10),
            TestToken);

        using var attempt = await fixture.FetchAsync(grant.Url, TestToken);

        attempt.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task DepositAsync(string objectKey)
    {
        var grant = await fixture.Transfer.CreateUploadGrantAsync(
            objectKey,
            _mediaType,
            _payload.Length,
            Convert.ToHexStringLower(SHA256.HashData(_payload)),
            TimeSpan.FromMinutes(10),
            TestToken);

        using var deposit = await fixture.DepositAsync(grant, _payload, TestToken);

        deposit.IsSuccessStatusCode.ShouldBeTrue(
            $"this deposit is a precondition rather than the assertion; the store answered " +
            $"{(int)deposit.StatusCode}.");
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}

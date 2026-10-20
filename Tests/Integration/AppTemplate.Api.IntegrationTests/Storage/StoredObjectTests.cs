using System.Net;
using System.Security.Cryptography;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Storage;

[Collection(ObjectStoreCollectionDefinition.Name)]
public sealed class StoredObjectTests(ObjectStoreFixture fixture)
{
    private const string _mediaType = "application/octet-stream";

    private static readonly byte[] _payload =
        "Forty-two bytes of nothing much, and a digest."u8.ToArray();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

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
    public async Task AWriteThroughTheStore_IsDescribedAndReadBack()
    {
        string objectKey = ObjectStoreFixture.KeyUnder(ObjectStoreFixture.UniquePrefix("written"));
        string checksum = Convert.ToHexStringLower(SHA256.HashData(_payload));

        var outcome = await fixture.Content.WriteAsync(
            objectKey,
            new MemoryStream(_payload),
            _mediaType,
            _payload.Length,
            checksum,
            TestToken);

        outcome.ShouldBe(ContentWriteOutcome.Written);
        (await fixture.Content.DescribeAsync(objectKey, TestToken))
            .ShouldBe(new StoredObjectDescription(_payload.Length, checksum));

        await using var read = await fixture.Content.OpenReadAsync(objectKey, TestToken);
        read.ShouldNotBeNull();
        using var copy = new MemoryStream();
        await read.CopyToAsync(copy, TestToken);
        copy.ToArray().ShouldBe(_payload);
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
    public async Task OpeningAKeyThatHoldsNothing_AnswersNull() =>
        (await fixture.Content.OpenReadAsync(
            ObjectStoreFixture.KeyUnder(ObjectStoreFixture.UniquePrefix("absent")),
            TestToken)).ShouldBeNull();

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
    public async Task Describing_AKeyNothingIsStoredUnder_AnswersAbsent()
    {
        string objectKey = ObjectStoreFixture.KeyUnder(ObjectStoreFixture.UniquePrefix("never-deposited"));

        var description = await fixture.Content.DescribeAsync(objectKey, TestToken);

        description.ShouldBeNull(
            "the port answers null for a deposit that never happened or one already reclaimed, and " +
            "the confirmation use case reads that as 'not yet there'. An exception instead would " +
            "turn a client confirming too early into a fault report.");
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

    [Fact]
    public async Task Deleting_AKeyNothingIsStoredUnder_Succeeds()
    {
        string objectKey = ObjectStoreFixture.KeyUnder(ObjectStoreFixture.UniquePrefix("never-there"));

        await Should.NotThrowAsync(() => fixture.Content.DeleteAsync(objectKey, TestToken));
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

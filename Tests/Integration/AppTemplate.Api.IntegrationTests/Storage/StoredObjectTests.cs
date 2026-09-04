using System.Net;
using System.Security.Cryptography;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Storage;

/// <summary>
/// What the store says it holds, and what removing an object leaves behind.
/// </summary>
/// <remarks>
/// <see cref="IFileContentStore.DescribeAsync"/> produces the only facts in the Files feature that
/// the client did not author — <c>StoredFile.ConfirmAvailable</c> compares them against the
/// declaration and refuses when they diverge — so a description that is wrong, or that cannot be
/// obtained at all, makes every deposit unconfirmable. That is not a claim a substitute can settle:
/// what a store records at deposit time, and what it will hand back afterwards, is the store's
/// behaviour and nobody else's.
/// </remarks>
[Collection(ObjectStoreCollectionDefinition.Name)]
public sealed class StoredObjectTests(ObjectStoreFixture fixture)
{
    private const string _mediaType = "application/octet-stream";

    private static readonly byte[] _payload =
        "Forty-two bytes of nothing much, and a digest."u8.ToArray();

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    /// <summary>
    /// A file deposited under the grant this module mints can be described — which is what
    /// confirmation needs, and what it could not do.
    /// </summary>
    /// <remarks>
    /// <b>This is the opposite of the test that used to stand here.</b> The grant signed
    /// <c>x-amz-sdk-checksum-algorithm: SHA256</c>, which names an algorithm and supplies nothing to
    /// check against: MinIO accepted the deposit, recorded no digest, and this call threw for want of
    /// one — so every file deposited through the two-step upload was left unconfirmable, and the
    /// feature could not complete against a real store at all. The old test characterised that and
    /// said in its own remarks to replace it with this one rather than delete it, so that the defect
    /// could not outlive its description in silence.
    /// </remarks>
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

    /// <summary>
    /// The grant binds the digest, so it authorises one body and not merely one of the right length.
    /// </summary>
    /// <remarks>
    /// <b>This is what stops content being swapped after inspection.</b> An upload grant lives
    /// <c>Storage:MaxGrantLifetime</c> — thirty minutes by default — while the inspection pass runs
    /// every minute, so for most of a grant's life the file it belongs to has already been examined
    /// and released. Measured before the digest was bound: a second deposit of different bytes of the
    /// same length answered <c>200</c>, and the file went on being served as inspected content.
    /// </remarks>
    [Fact]
    public async Task ASecondDepositOfDifferentBytes_IsRefusedByTheStore()
    {
        string objectKey = ObjectStoreFixture.KeyUnder(ObjectStoreFixture.UniquePrefix("swap"));

        var grant = await fixture.Content.CreateUploadGrantAsync(
            objectKey,
            _mediaType,
            _payload.Length,
            Convert.ToHexStringLower(SHA256.HashData(_payload)),
            TimeSpan.FromMinutes(10),
            TestToken);

        using var honest = await fixture.DepositAsync(grant, _payload, TestToken);

        honest.IsSuccessStatusCode.ShouldBeTrue(
            $"a precondition rather than the assertion; the store answered {(int)honest.StatusCode}.");

        // The same length, so the signature's Content-Length still matches: the digest is the only
        // thing standing between this grant and any body the holder cares to send.
        byte[] swapped = [.. Enumerable.Repeat((byte)'Z', _payload.Length)];

        using var swap = await fixture.DepositAsync(grant, swapped, TestToken);

        swap.IsSuccessStatusCode.ShouldBeFalse(
            $"the store answered {(int)swap.StatusCode} to bytes whose digest is not the one the " +
            "grant was signed for. Accepting them would let a holder replace the content of a file " +
            $"that has already been inspected and released: " +
            await swap.Content.ReadAsStringAsync(TestToken));

        // And the object still holds what was honestly deposited, rather than a partial write.
        var description = await fixture.Content.DescribeAsync(objectKey, TestToken);

        description.ShouldNotBeNull();
        description.Checksum.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(_payload)));
    }

    /// <summary>
    /// The encoding boundary, which is where two correct implementations disagree silently.
    /// </summary>
    /// <remarks>
    /// The store speaks base64 and the port asks for lower-case hexadecimal — the same thirty-two
    /// bytes written two ways. Nothing in process would notice a mix-up: the adapter would hand
    /// confirmation a well-formed string that never equals the declared digest, and every upload that
    /// had actually arrived would be refused for a mismatch naming nothing.
    /// </remarks>
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

        // And not merely invisible to the description: a grant already minted for it must stop
        // resolving too, or a download URL handed out before the deletion would outlive it.
        var grant = await fixture.Content.CreateDownloadGrantAsync(
            objectKey,
            "gone.bin",
            _mediaType,
            TimeSpan.FromMinutes(10),
            TestToken);

        using var attempt = await fixture.FetchAsync(grant.Url, TestToken);

        attempt.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Deleting nothing succeeds, because two uncoordinated callers reach the same key.
    /// </summary>
    /// <remarks>
    /// The fast path that reacts to a deletion and the sweep that reclaims unreferenced content are
    /// not coordinated with each other, and the port says so. S3 itself answers 204 for a key that is
    /// not there; a compatible store that answered 404 instead must not turn the second caller into
    /// an error, and the adapter swallows that case for exactly this reason.
    /// </remarks>
    [Fact]
    public async Task Deleting_AKeyNothingIsStoredUnder_Succeeds()
    {
        string objectKey = ObjectStoreFixture.KeyUnder(ObjectStoreFixture.UniquePrefix("never-there"));

        await Should.NotThrowAsync(() => fixture.Content.DeleteAsync(objectKey, TestToken));
    }

    private async Task DepositAsync(string objectKey)
    {
        var grant = await fixture.Content.CreateUploadGrantAsync(
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
}

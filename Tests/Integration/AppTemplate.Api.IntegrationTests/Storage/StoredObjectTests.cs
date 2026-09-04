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
    /// <b>This test documents a defect, and it is written to fail the day the defect is fixed.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// The grant this module mints signs <c>x-amz-sdk-checksum-algorithm: SHA256</c> and nothing
    /// else, and that header names an algorithm without supplying a value. A store has nothing to
    /// record from it — MinIO accepts the deposit and stores no digest — so
    /// <see cref="IFileContentStore.DescribeAsync"/> reaches its own guard and throws, and every file
    /// deposited through the two-step upload is left unconfirmable.
    /// </para>
    /// <para>
    /// The next test shows the store does record a SHA-256 when it is given one, so the gap is in
    /// what the grant asks for rather than in what MinIO can do. The fix belongs in the adapter:
    /// <c>RegisterFileUseCase</c> already holds the client's declared checksum
    /// (<c>Sha256Checksum.Create(command.Checksum)</c>), so the grant can sign
    /// <c>x-amz-checksum-sha256</c> with that value — which also makes the store refuse a deposit of
    /// the wrong bytes outright, exactly as it already refuses one of the wrong length.
    /// </para>
    /// <para>
    /// <b>Replace this test with its opposite in the change that fixes the adapter.</b> A
    /// characterisation test outlives its subject silently otherwise, and this one would then be
    /// holding the defect in place.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Describing_AnObjectDepositedUnderTheGrantThisModuleMints_FailsForWantOfADigest()
    {
        string objectKey = ObjectStoreFixture.KeyUnder(ObjectStoreFixture.UniquePrefix("no-digest"));

        await DepositAsync(objectKey);

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() =>
            fixture.Content.DescribeAsync(objectKey, TestToken));

        // The adapter refuses to answer with anything else, deliberately: an entity tag is not a
        // SHA-256, and passing one through would fail every confirmation with a mismatch that names
        // nothing.
        thrown.Message.ShouldContain("carries no SHA-256 checksum", Case.Sensitive);
    }

    /// <summary>
    /// The half that works, and the proof that the store is not what is missing.
    /// </summary>
    /// <remarks>
    /// The deposit here carries <c>x-amz-checksum-sha256</c>, which no grant this module mints asks
    /// for — see the test above. It is deposited that way to separate two questions the failing case
    /// cannot: whether the store can record and report a SHA-256 at all, and whether this module
    /// asks it to. The answers are yes and no.
    /// </remarks>
    [Fact]
    public async Task Describing_AnObjectWhoseDigestTheStoreWasGiven_ReportsTheRealLengthAndDigest()
    {
        string objectKey = ObjectStoreFixture.KeyUnder(ObjectStoreFixture.UniquePrefix("with-digest"));

        byte[] digest = SHA256.HashData(_payload);

        await DepositAsync(
            objectKey,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["x-amz-checksum-sha256"] = Convert.ToBase64String(digest),
            });

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

        byte[] digest = SHA256.HashData(_payload);

        await DepositAsync(
            objectKey,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["x-amz-checksum-sha256"] = Convert.ToBase64String(digest),
            });

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

    private async Task DepositAsync(string objectKey, IReadOnlyDictionary<string, string>? extraHeaders = null)
    {
        var grant = await fixture.Content.CreateUploadGrantAsync(
            objectKey,
            _mediaType,
            _payload.Length,
            TimeSpan.FromMinutes(10),
            TestToken);

        using var deposit = await fixture.DepositAsync(grant, _payload, TestToken, extraHeaders);

        deposit.IsSuccessStatusCode.ShouldBeTrue(
            $"this deposit is a precondition rather than the assertion; the store answered " +
            $"{(int)deposit.StatusCode}.");
    }
}

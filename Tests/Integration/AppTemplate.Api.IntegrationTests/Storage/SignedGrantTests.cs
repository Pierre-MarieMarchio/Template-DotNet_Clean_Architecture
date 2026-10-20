using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Storage;

[Collection(ObjectStoreCollectionDefinition.Name)]
public sealed class SignedGrantTests(ObjectStoreFixture fixture)
{
    private const string _mediaType = "application/octet-stream";

    private static readonly byte[] _payload =
        "The quick brown fox jumps over the lazy dog, and then deposits itself in a bucket."u8.ToArray();

    private static readonly string _checksum = Convert.ToHexStringLower(SHA256.HashData(_payload));

    private static readonly TimeSpan _expiryCap = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan _shortLifetime = TimeSpan.FromSeconds(3);

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnUploadGrant_IsAcceptedByTheStore()
    {
        string objectKey = ObjectStoreFixture.KeyUnder(ObjectStoreFixture.UniquePrefix("accepted"));

        var grant = await fixture.Transfer.CreateUploadGrantAsync(
            objectKey,
            _mediaType,
            _payload.Length,
            _checksum,
            TimeSpan.FromMinutes(10),
            TestToken);

        using var deposit = await fixture.DepositAsync(grant, _payload, TestToken);

        deposit.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            $"the whole two-step upload rests on this one exchange. The store answered " +
            $"{(int)deposit.StatusCode} to a URL this module signed and the headers the grant " +
            $"itself named: {await deposit.Content.ReadAsStringAsync(TestToken)}");
    }

    [Fact]
    public async Task ADownloadGrant_RendersTheDepositedBytes()
    {
        string objectKey = ObjectStoreFixture.KeyUnder(ObjectStoreFixture.UniquePrefix("round-trip"));

        await DepositAsync(objectKey);

        var grant = await fixture.Transfer.CreateDownloadGrantAsync(
            objectKey,
            "quarterly report.txt",
            "text/plain",
            TimeSpan.FromMinutes(10),
            TestToken);

        using var download = await fixture.FetchAsync(grant.Url, TestToken);

        download.StatusCode.ShouldBe(HttpStatusCode.OK);

        byte[] returned = await download.Content.ReadAsByteArrayAsync(TestToken);

        returned.ShouldBe(
            _payload,
            "the bytes are what the client came for, and they travel between the client and the " +
            "store without passing through this process at all. Anything short of identical here " +
            "is content this application cannot see going wrong.");
    }

    [Fact]
    public async Task ADownloadGrant_MakesTheStoreAnswerWithAnAttachment()
    {
        string objectKey = ObjectStoreFixture.KeyUnder(ObjectStoreFixture.UniquePrefix("disposition"));

        await DepositAsync(objectKey);

        var grant = await fixture.Transfer.CreateDownloadGrantAsync(
            objectKey,
            "rapport été.txt",
            "text/plain",
            TimeSpan.FromMinutes(10),
            TestToken);

        using var download = await fixture.FetchAsync(grant.Url, TestToken);

        download.Content.Headers.ContentType?.MediaType.ShouldBe(
            "text/plain",
            "the object was deposited as application/octet-stream. The type the client sees is the " +
            "one the grant signed, which is what makes it the application's decision.");

        string? disposition = download.Content.Headers.ContentDisposition?.ToString();

        disposition.ShouldNotBeNull();

        disposition.ShouldStartWith("attachment", Case.Sensitive);

        disposition.ShouldContain("filename*=UTF-8''rapport%20%C3%A9t%C3%A9.txt");
    }

    [Fact]
    public async Task ADepositOfADifferentLength_IsRefused()
    {
        string objectKey = ObjectStoreFixture.KeyUnder(ObjectStoreFixture.UniquePrefix("wrong-length"));

        var grant = await fixture.Transfer.CreateUploadGrantAsync(
            objectKey,
            _mediaType,
            _payload.Length,
            _checksum,
            TimeSpan.FromMinutes(10),
            TestToken);

        using var deposit = await fixture.DepositAsync(grant, [.. _payload, .. _payload], TestToken);

        deposit.StatusCode.ShouldBe(
            HttpStatusCode.Forbidden,
            "the length is bound into the signature so that a client which declared one size and " +
            "deposits another is refused by the store with nothing written — rather than at " +
            "confirmation, with the bytes already paid for and a row that cannot be confirmed.");
    }

    [Fact]
    public async Task ADepositThatOmitsARequiredHeader_IsRefused()
    {
        string objectKey = ObjectStoreFixture.KeyUnder(ObjectStoreFixture.UniquePrefix("missing-header"));

        var grant = await fixture.Transfer.CreateUploadGrantAsync(
            objectKey,
            _mediaType,
            _payload.Length,
            _checksum,
            TimeSpan.FromMinutes(10),
            TestToken);

        grant.RequiredHeaders.ContainsKey("Content-Type").ShouldBeTrue(
            "the case below drops this one, so a grant that stopped naming it would make the test " +
            "assert nothing while staying green.");

        var withoutContentType = grant with
        {
            RequiredHeaders = grant.RequiredHeaders
                .Where(header => !string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(header => header.Key, header => header.Value, StringComparer.OrdinalIgnoreCase),
        };

        using var deposit = await fixture.DepositAsync(withoutContentType, _payload, TestToken);

        deposit.IsSuccessStatusCode.ShouldBeFalse(
            $"a header the grant names is one the signature covers, and the store answered " +
            $"{(int)deposit.StatusCode} to a deposit that dropped Content-Type. Accepting it would " +
            "make the media type bound into the grant a decoration a client can leave out: " +
            await deposit.Content.ReadAsStringAsync(TestToken));
    }

    [Fact]
    public async Task TheSameObjectWithoutASignature_IsRefused()
    {
        string objectKey = ObjectStoreFixture.KeyUnder(ObjectStoreFixture.UniquePrefix("unsigned"));

        await DepositAsync(objectKey);

        var grant = await fixture.Transfer.CreateDownloadGrantAsync(
            objectKey,
            "anything.txt",
            "text/plain",
            TimeSpan.FromMinutes(10),
            TestToken);

        string unsigned = grant.Url[..grant.Url.IndexOf('?', StringComparison.Ordinal)];

        using var attempt = await fixture.FetchAsync(unsigned, TestToken);

        attempt.StatusCode.ShouldBe(
            HttpStatusCode.Forbidden,
            "the object was reachable at this exact address a moment ago with a signature on it. " +
            "Served without one, every grant this module mints is a formality over a bucket the " +
            "whole internet can read.");
    }

    [Fact]
    public async Task AGrantWhoseLifetimeHasElapsed_IsRefused()
    {
        string objectKey = ObjectStoreFixture.KeyUnder(ObjectStoreFixture.UniquePrefix("expired"));

        await DepositAsync(objectKey);

        var control = await fixture.Transfer.CreateDownloadGrantAsync(
            objectKey,
            "anything.txt",
            "text/plain",
            TimeSpan.FromMinutes(10),
            TestToken);

        using (var served = await fixture.FetchAsync(control.Url, TestToken))
        {
            served.StatusCode.ShouldBe(
                HttpStatusCode.OK,
                "a grant over this object is refused while nowhere near its deadline, so the "
                + "refusal the assertion below waits for would be evidence of nothing.");
        }

        var grant = await fixture.Transfer.CreateDownloadGrantAsync(
            objectKey,
            "anything.txt",
            "text/plain",
            _shortLifetime,
            TestToken);

        var elapsed = Stopwatch.StartNew();
        HttpStatusCode last;

        do
        {
            using var attempt = await fixture.FetchAsync(grant.Url, TestToken);
            last = attempt.StatusCode;

            if (last != HttpStatusCode.Forbidden)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), TestToken);
            }
        }
        while (last != HttpStatusCode.Forbidden && elapsed.Elapsed < _expiryCap);

        last.ShouldBe(
            HttpStatusCode.Forbidden,
            $"a grant signed to expire at {grant.ExpiresAt:O} was still being served " +
            $"{elapsed.Elapsed.TotalSeconds:F0} seconds later. A signed URL ends up in a browser " +
            "history, a referrer header and a proxy log, and the expiry is the only thing that " +
            "makes those copies stop being worth anything.");
    }

    private async Task DepositAsync(string objectKey)
    {
        var grant = await fixture.Transfer.CreateUploadGrantAsync(
            objectKey,
            _mediaType,
            _payload.Length,
            _checksum,
            TimeSpan.FromMinutes(10),
            TestToken);

        using var deposit = await fixture.DepositAsync(grant, _payload, TestToken);

        deposit.IsSuccessStatusCode.ShouldBeTrue(
            $"this deposit is a precondition rather than the assertion; the store answered " +
            $"{(int)deposit.StatusCode}.");
    }
}

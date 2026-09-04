using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Storage;

/// <summary>
/// The two-step upload, against a real store: a grant this module signs is one the store accepts,
/// and the bytes deposited under it come back byte for byte.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only a real store can answer this.</b> The unit tests read the URL a grant carries and assert
/// its shape, which is all they can do without a store — the signature is computed locally, so a
/// wrong one is indistinguishable from a right one until something checks it. Every assertion here
/// is about the half nothing else can see: whether the store agrees.
/// </para>
/// <para>
/// The refusals matter as much as the acceptance. A grant is a bearer right, and its whole value is
/// that it authorises <em>one</em> deposit of <em>one</em> shape — so a suite that only proved the
/// happy path would be equally green against a bucket that accepted anything from anyone.
/// </para>
/// </remarks>
[Collection(ObjectStoreCollectionDefinition.Name)]
public sealed class SignedGrantTests(ObjectStoreFixture fixture)
{
    private const string _mediaType = "application/octet-stream";

    private static readonly byte[] _payload =
        "The quick brown fox jumps over the lazy dog, and then deposits itself in a bucket."u8.ToArray();

    /// <summary>
    /// The digest of <see cref="_payload"/>, computed rather than written down. A grant binds it, so a
    /// constant that drifted from the bytes would mint a grant no honest deposit could satisfy.
    /// </summary>
    private static readonly string _checksum = Convert.ToHexStringLower(SHA256.HashData(_payload));

    /// <summary>
    /// How long a grant is given to stop working before the wait is called a failure.
    /// </summary>
    /// <remarks>
    /// Not a measurement of the store's punctuality: the grant below is signed for
    /// <see cref="_shortLifetime"/>, and a store that honours the expiry refuses on the first request
    /// past it. The cap is set orders of magnitude above that so that only a store ignoring the
    /// deadline entirely can reach it — a cap of five seconds would be a bet on this machine, which is
    /// a bet this repository has already lost once.
    /// </remarks>
    private static readonly TimeSpan _expiryCap = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How much earlier than the instant a grant announces the store may stop honouring it.
    /// </summary>
    /// <remarks>
    /// A Signature Version 4 URL carries <c>X-Amz-Date</c> floored to the whole second and
    /// <c>X-Amz-Expires</c> as a whole number of seconds, so the deadline the store computes is the
    /// signing second plus that count — up to one second before the instant <c>ExpiryFor</c> put in
    /// the grant, which it takes from the unfloored clock. Measured, not assumed: a grant asked for
    /// with a one-second lifetime is signed <c>X-Amz-Expires=1</c> against a floored date and is
    /// refused before its own <c>ExpiresAt</c> on most runs.
    /// <para>
    /// This is why the lifetime below is not one second. At one second the window in which a refusal
    /// unambiguously means "never valid" is empty, and the check above could only ever be vacuous or
    /// wrong.
    /// </para>
    /// </remarks>
    private static readonly TimeSpan _signatureDateGranularity = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Long enough to leave a window — <c>lifetime</c> minus <see cref="_signatureDateGranularity"/> —
    /// in which the store serving the object is the only correct answer, so a refusal inside it is
    /// evidence of a grant that never worked rather than of an expiry being honoured.
    /// </summary>
    private static readonly TimeSpan _shortLifetime = TimeSpan.FromSeconds(3);

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnUploadGrant_IsAcceptedByTheStore()
    {
        string objectKey = ObjectStoreFixture.KeyUnder(ObjectStoreFixture.UniquePrefix("accepted"));

        var grant = await fixture.Content.CreateUploadGrantAsync(
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

        var grant = await fixture.Content.CreateDownloadGrantAsync(
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

    /// <summary>
    /// The overrides the download grant signs are the store's answer, not a suggestion.
    /// </summary>
    /// <remarks>
    /// They are what stops an uploader choosing what a downloader's browser executes: the declared
    /// media type is the uploader's own claim about bytes nothing here has read, and the disposition
    /// is what makes the browser save rather than render it. A store that ignored either would leave
    /// that decision with whoever deposited the file, and no unit test can tell the difference —
    /// signing an override and honouring one are two different parties' jobs.
    /// </remarks>
    [Fact]
    public async Task ADownloadGrant_MakesTheStoreAnswerWithAnAttachment()
    {
        string objectKey = ObjectStoreFixture.KeyUnder(ObjectStoreFixture.UniquePrefix("disposition"));

        await DepositAsync(objectKey);

        var grant = await fixture.Content.CreateDownloadGrantAsync(
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

        // Inline would hand an uploader the right to have a viewer's browser render whatever was
        // deposited.
        disposition.ShouldStartWith("attachment", Case.Sensitive);

        // The RFC 6266 pair, and the reason there are two: the quoted form is ASCII for clients that
        // parse nothing else, and the starred one carries the name the user actually chose. A store
        // that dropped the second would silently rename every file with a non-ASCII character in it.
        disposition.ShouldContain("filename*=UTF-8''rapport%20%C3%A9t%C3%A9.txt");
    }

    [Fact]
    public async Task ADepositOfADifferentLength_IsRefused()
    {
        string objectKey = ObjectStoreFixture.KeyUnder(ObjectStoreFixture.UniquePrefix("wrong-length"));

        var grant = await fixture.Content.CreateUploadGrantAsync(
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

    /// <summary>
    /// The headers a grant lists are the headers the signature covers, and the store checks.
    /// </summary>
    /// <remarks>
    /// The unit tests hold that the two lists agree because the grant is built from the signed
    /// request rather than restated. What they cannot hold is that the list means anything: only the
    /// store recomputing the signature over what actually arrived turns it into a requirement.
    /// </remarks>
    [Fact]
    public async Task ADepositThatOmitsARequiredHeader_IsRefused()
    {
        string objectKey = ObjectStoreFixture.KeyUnder(ObjectStoreFixture.UniquePrefix("missing-header"));

        var grant = await fixture.Content.CreateUploadGrantAsync(
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

        // Refused, rather than refused with one particular status: a missing signed header is a
        // malformed request to one store and a bad signature to another — MinIO answers 400 here and
        // AWS S3 answers 403 SignatureDoesNotMatch — and pinning either would make this a test of
        // which store is running.
        deposit.IsSuccessStatusCode.ShouldBeFalse(
            $"a header the grant names is one the signature covers, and the store answered " +
            $"{(int)deposit.StatusCode} to a deposit that dropped Content-Type. Accepting it would " +
            "make the media type bound into the grant a decoration a client can leave out: " +
            await deposit.Content.ReadAsStringAsync(TestToken));
    }

    /// <summary>
    /// The signature is what authorises, not the URL, and not the bucket.
    /// </summary>
    /// <remarks>
    /// A publicly readable bucket would make every signed URL in this template decoration — the
    /// deployment guidance says so and <c>docker-compose.yml</c>'s <c>mc anonymous set none</c> is
    /// the development stack acting on it. Nothing in-process can check that; this can.
    /// </remarks>
    [Fact]
    public async Task TheSameObjectWithoutASignature_IsRefused()
    {
        string objectKey = ObjectStoreFixture.KeyUnder(ObjectStoreFixture.UniquePrefix("unsigned"));

        await DepositAsync(objectKey);

        var grant = await fixture.Content.CreateDownloadGrantAsync(
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

    /// <summary>
    /// An expired grant stops working, which is the only thing that limits what a leaked URL is
    /// still worth.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The lifetime is <see cref="_shortLifetime"/> rather than the shortest the port accepts, for
    /// the reason <see cref="_signatureDateGranularity"/> gives, and the loop then asks until the
    /// store refuses rather than sleeping for a guessed interval. The wait is on the condition, so a
    /// store that expires promptly costs one extra request and a slow machine costs a few more.
    /// </para>
    /// <para>
    /// <b>The lifetime is not the operator's ceiling.</b> <c>MaxGrantLifetime</c> clamps downwards
    /// only, so asking for a second gets a second whatever the ceiling is, and this test needs no
    /// configuration of its own.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task AGrantWhoseLifetimeHasElapsed_IsRefused()
    {
        string objectKey = ObjectStoreFixture.KeyUnder(ObjectStoreFixture.UniquePrefix("expired"));

        await DepositAsync(objectKey);

        var grant = await fixture.Content.CreateDownloadGrantAsync(
            objectKey,
            "anything.txt",
            "text/plain",
            _shortLifetime,
            TestToken);

        var elapsed = Stopwatch.StartNew();
        HttpStatusCode last;
        var refusedBeforeItsDeadline = false;

        do
        {
            using var attempt = await fixture.FetchAsync(grant.Url, TestToken);
            last = attempt.StatusCode;

            // A refusal is only evidence of expiry if it arrives while the grant was still supposed
            // to work. Without this the assertion below is satisfied by a grant that never worked at
            // all — a signature computed wrong, a lifetime that reached the store as nothing — since
            // the first request would answer Forbidden and the loop would end on its own exit
            // condition having proved nothing about any deadline. Verified by tampering with the
            // signature: this is what turns red, and the assertion below stays green.
            //
            // Recorded rather than asserted inside the loop, so that a first request which lands
            // after the window has closed observes nothing instead of failing. That is what keeps
            // this off the machine's clock: a loaded run proves less, never something false.
            if (last == HttpStatusCode.Forbidden && DateTimeOffset.UtcNow < grant.ExpiresAt - _signatureDateGranularity)
            {
                refusedBeforeItsDeadline = true;
            }

            if (last != HttpStatusCode.Forbidden)
            {
                // A polling interval, not a guess at when the grant expires: the loop ends on the
                // store's answer, and this only keeps a passing run from spending its one second
                // hammering the container.
                await Task.Delay(TimeSpan.FromMilliseconds(100), TestToken);
            }
        }
        // The exit condition is the thing being asserted, not "the answer changed". Twelve test
        // projects and four containers run at once here, and a store under that much start-up load
        // can answer a request with something that is neither the object nor a refusal. Ending the
        // loop on any non-OK status would turn one of those into a failed assertion about expiry —
        // a flake, and one that would read as if the deadline had been ignored.
        while (last != HttpStatusCode.Forbidden && elapsed.Elapsed < _expiryCap);

        refusedBeforeItsDeadline.ShouldBeFalse(
            $"the store refused this grant before {grant.ExpiresAt:O}, the instant it was signed to " +
            "expire at. That is not the expiry being honoured — it is a grant that was never valid, " +
            "and the assertion below would have called it a pass.");

        last.ShouldBe(
            HttpStatusCode.Forbidden,
            $"a grant signed to expire at {grant.ExpiresAt:O} was still being served " +
            $"{elapsed.Elapsed.TotalSeconds:F0} seconds later. A signed URL ends up in a browser " +
            "history, a referrer header and a proxy log, and the expiry is the only thing that " +
            "makes those copies stop being worth anything.");
    }

    private async Task DepositAsync(string objectKey)
    {
        var grant = await fixture.Content.CreateUploadGrantAsync(
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

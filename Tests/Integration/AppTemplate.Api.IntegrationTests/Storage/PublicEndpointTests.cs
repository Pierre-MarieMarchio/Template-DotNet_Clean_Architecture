using System.Net;
using System.Security.Cryptography;
using AppTemplate.Application.Features.Files.Ports.FileContentInventory;
using AppTemplate.Application.Features.Files.Ports.FileContentStore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Storage;

/// <summary>
/// Why <c>Storage:PublicEndpoint</c> exists, measured against a real store rather than argued from
/// the specification.
/// </summary>
/// <remarks>
/// <para>
/// A Signature Version 4 presigned URL covers the host it was signed for — <c>host</c> is always in
/// <c>X-Amz-SignedHeaders</c> — so the name has to be right at signing time. The first test below is
/// the measurement that settles it: one container, one port, one object, and the only thing that
/// changes is the name the request arrives under.
/// </para>
/// <para>
/// <b>The name is changed with a <c>Host</c> header and not with DNS.</b> The connection still goes
/// to the address the URL names, so these tests need no resolvable second name, no hosts file and no
/// assumption about where Testcontainers published the port — and what they vary is exactly the one
/// input the signature covers.
/// </para>
/// </remarks>
[Collection(ObjectStoreCollectionDefinition.Name)]
public sealed class PublicEndpointTests(ObjectStoreFixture fixture)
{
    /// <summary>
    /// A name this machine cannot resolve, deliberately: nothing here may reach the store by
    /// resolving it, or the tests would be measuring DNS instead of the signature.
    /// </summary>
    private const string _publicName = "files.example:9000";

    private const string _publicEndpoint = $"http://{_publicName}";

    private const string _mediaType = "application/octet-stream";

    private static readonly byte[] _payload = "Signed for one name, presented under another."u8.ToArray();

    /// <summary>
    /// The digest of <see cref="_payload"/>, computed rather than written down. A grant binds it, so a
    /// constant that drifted from the bytes would mint a grant no honest deposit could satisfy.
    /// </summary>
    private static readonly string _checksum = Convert.ToHexStringLower(SHA256.HashData(_payload));

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    /// <summary>
    /// <b>The measurement the whole setting rests on.</b> Same store, same port, same object, same
    /// signature — and a request that arrives under a different name is refused.
    /// </summary>
    /// <remarks>
    /// It is what rules out every cheaper fix: an API that rewrote the host in the URL it hands back,
    /// a reverse proxy that rewrote it in flight, a client that substituted its own name. All three
    /// produce this exact response, so the endpoint a URL is signed for has to be right when the
    /// signature is computed and can be corrected nowhere afterwards.
    /// </remarks>
    [Fact]
    public async Task AGrantFollowedUnderANameItWasNotSignedFor_IsRefused()
    {
        string objectKey = ObjectStoreFixture.KeyUnder(ObjectStoreFixture.UniquePrefix("wrong-host"));

        await DepositAsync(objectKey);

        var grant = await fixture.Content.CreateDownloadGrantAsync(
            objectKey,
            "anything.bin",
            _mediaType,
            TimeSpan.FromMinutes(10),
            TestToken);

        using var asSigned = await fixture.FetchAsync(grant.Url, TestToken);

        asSigned.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            "the URL has to work under the name it was signed for, or the refusal below would say " +
            "nothing about the name at all.");

        using var underAnotherName = await fixture.FetchAsync(grant.Url, TestToken, _publicName);

        string body = await underAnotherName.Content.ReadAsStringAsync(TestToken);

        underAnotherName.StatusCode.ShouldBe(
            HttpStatusCode.Forbidden,
            $"the same object, at the same address, one request apart. The store answered " +
            $"{(int)underAnotherName.StatusCode}: {body}");

        body.ShouldContain(
            "SignatureDoesNotMatch",
            Case.Sensitive,
            "the refusal has to be about the signature. A 403 for any other reason would leave the " +
            "claim that the host is covered by it unmeasured.");
    }

    /// <summary>
    /// The other half: signing for the public name makes the very same store accept the very same
    /// request under it.
    /// </summary>
    /// <remarks>
    /// The URL is routed back to the address this machine reaches the container at, and only the
    /// <c>Host</c> header carries the public name — which is what a deployment gets for free, where
    /// that name really does resolve to an ingress in front of the same store.
    /// </remarks>
    [Fact]
    public async Task AGrantSignedForThePublicEndpoint_IsAcceptedUnderThatName()
    {
        using var published = fixture.ComposePublishingAt(_publicEndpoint);
        using var scope = published.CreateScope();

        var content = scope.ServiceProvider.GetRequiredService<IFileContentStore>();

        string prefix = ObjectStoreFixture.UniquePrefix("published");
        string objectKey = ObjectStoreFixture.KeyUnder(prefix);

        var upload = await content.CreateUploadGrantAsync(
            objectKey,
            _mediaType,
            _payload.Length,
            _checksum,
            TimeSpan.FromMinutes(10),
            TestToken);

        new Uri(upload.Url).Authority.ShouldBe(
            _publicName,
            "the grant is minted for the public name; a URL still naming the internal one would make " +
            "the deposit below succeed for the wrong reason.");

        using var deposit = await fixture.DepositAsync(
            upload with { Url = RoutedToTheStore(upload.Url) },
            _payload,
            TestToken,
            hostHeader: _publicName);

        deposit.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            $"the store answered {(int)deposit.StatusCode} to a deposit signed for the name it " +
            $"arrived under: {await deposit.Content.ReadAsStringAsync(TestToken)}");

        var download = await content.CreateDownloadGrantAsync(
            objectKey,
            "published.bin",
            _mediaType,
            TimeSpan.FromMinutes(10),
            TestToken);

        using var fetched = await fixture.FetchAsync(
            RoutedToTheStore(download.Url),
            TestToken,
            _publicName);

        fetched.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await fetched.Content.ReadAsByteArrayAsync(TestToken)).ShouldBe(_payload);

        // And the split is a split: the calls this process makes itself still go over the internal
        // endpoint, which is the only one it can resolve. A listing that came back empty would mean
        // the inventory had followed the public name into nothing.
        var inventory = scope.ServiceProvider.GetRequiredService<IFileContentInventory>();

        var listed = await inventory.ListKeysAsync(prefix, continuationToken: null, 10, TestToken);

        listed.Items.ShouldBe([objectKey]);
    }

    /// <summary>
    /// The same address, with the name the URL was signed for carried in the <c>Host</c> header. A
    /// string rewrite rather than a <see cref="UriBuilder"/>, which would re-encode a query whose
    /// escaping the signature was computed over.
    /// </summary>
    private string RoutedToTheStore(string signedUrl)
    {
        var signed = new Uri(signedUrl);
        var store = new Uri(fixture.StoreEndpoint);

        return $"{store.Scheme}://{store.Authority}{signed.PathAndQuery}";
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

using System.Net;
using System.Security.Cryptography;
using AppTemplate.Application.Features.Files.Ports.FileContentInventory;
using AppTemplate.Application.Features.Files.Ports.FileDirectTransfer;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.IntegrationTests.Storage;

[Collection(ObjectStoreCollectionDefinition.Name)]
public sealed class PublicEndpointTests(ObjectStoreFixture fixture)
{
    private const string _publicName = "files.example:9000";

    private const string _publicEndpoint = $"http://{_publicName}";

    private const string _mediaType = "application/octet-stream";

    private static readonly byte[] _payload = "Signed for one name, presented under another."u8.ToArray();

    private static readonly string _checksum = Convert.ToHexStringLower(SHA256.HashData(_payload));

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AGrantFollowedUnderANameItWasNotSignedFor_IsRefused()
    {
        string objectKey = ObjectStoreFixture.KeyUnder(ObjectStoreFixture.UniquePrefix("wrong-host"));

        await DepositAsync(objectKey);

        var grant = await fixture.Transfer.CreateDownloadGrantAsync(
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

    [Fact]
    public async Task AGrantSignedForThePublicEndpoint_IsAcceptedUnderThatName()
    {
        using var published = fixture.ComposePublishingAt(_publicEndpoint);
        using var scope = published.CreateScope();

        var transfer = scope.ServiceProvider.GetRequiredService<IFileDirectTransfer>();

        string prefix = ObjectStoreFixture.UniquePrefix("published");
        string objectKey = ObjectStoreFixture.KeyUnder(prefix);

        var upload = await transfer.CreateUploadGrantAsync(
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

        var download = await transfer.CreateDownloadGrantAsync(
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

        var inventory = scope.ServiceProvider.GetRequiredService<IFileContentInventory>();

        var listed = await inventory.ListKeysAsync(prefix, startAfter: null, 10, TestToken);

        listed.Items.ShouldBe([objectKey]);
    }

    private string RoutedToTheStore(string signedUrl)
    {
        var signed = new Uri(signedUrl);
        var store = new Uri(fixture.StoreEndpoint);

        return $"{store.Scheme}://{store.Authority}{signed.PathAndQuery}";
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

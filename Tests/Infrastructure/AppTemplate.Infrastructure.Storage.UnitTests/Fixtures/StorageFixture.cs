using Amazon.S3;
using AppTemplate.Infrastructure.Storage.Common.Factories;
using AppTemplate.Infrastructure.Storage.Common.Options;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Storage.UnitTests.Fixtures;

/// <summary>
/// A configured module, the way a host would have configured it.
/// <para>
/// The credentials are fabricated and that changes nothing about what is being tested: a signature
/// is a keyed hash of the request, so any key produces a real, well-formed presigned URL. What the
/// key decides is whether a store would accept it, and no store is reached here.
/// </para>
/// </summary>
internal static class StorageFixture
{
    internal const string Bucket = "app-files";

    internal const string Region = "eu-west-3";

    /// <summary>A key under the scheme <c>ObjectKey</c> mints, so the tests read like real calls.</summary>
    internal const string ObjectKey = "t0/202608/9f2c1d7a4b6e8f0132547698badcfe10";

    internal static StorageOptions Options(Action<StorageOptions>? adjust = null)
    {
        var options = new StorageOptions
        {
            BucketName = Bucket,
            Region = Region,
            AccessKeyId = "test-access-key-id",
            SecretAccessKey = "test-secret-access-key",
        };

        adjust?.Invoke(options);

        return options;
    }

    internal static IOptions<StorageOptions> Wrap(StorageOptions options) =>
        Microsoft.Extensions.Options.Options.Create(options);

    /// <summary>
    /// The client this process would call the store with — the one the adapters use for a metadata
    /// read, a delete and a listing.
    /// </summary>
    internal static IAmazonS3 Client(StorageOptions options) => BucketClientFactory.Create(options);

    /// <summary>
    /// The real client every grant is minted by, built on the endpoint URLs are signed for. It signs
    /// locally and opens nothing.
    /// </summary>
    internal static IAmazonS3 Signer(StorageOptions options) => BucketClientFactory.CreateForSigning(options);
}

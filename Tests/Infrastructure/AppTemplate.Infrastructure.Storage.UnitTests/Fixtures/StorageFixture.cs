using Amazon.S3;
using AppTemplate.Infrastructure.Storage.Common.Factories;
using AppTemplate.Infrastructure.Storage.Common.Options;
using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Storage.UnitTests.Fixtures;

internal static class StorageFixture
{
    internal const string Bucket = "app-files";

    internal const string Region = "eu-west-3";

    internal const string ObjectKey = "t0/202608/9f2c1d7a4b6e8f0132547698badcfe10";

    internal static FilesStorageS3Options Options(Action<FilesStorageS3Options>? adjust = null)
    {
        var options = new FilesStorageS3Options
        {
            BucketName = Bucket,
            Region = Region,
            AccessKeyId = "test-access-key-id",
            SecretAccessKey = "test-secret-access-key",
        };

        adjust?.Invoke(options);

        return options;
    }

    internal static IOptions<FilesStorageS3Options> Wrap(FilesStorageS3Options options) =>
        Microsoft.Extensions.Options.Options.Create(options);

    internal static IAmazonS3 Client(FilesStorageS3Options options) => BucketClientFactory.Create(options);

    internal static IAmazonS3 Signer(FilesStorageS3Options options) => BucketClientFactory.CreateForSigning(options);
}

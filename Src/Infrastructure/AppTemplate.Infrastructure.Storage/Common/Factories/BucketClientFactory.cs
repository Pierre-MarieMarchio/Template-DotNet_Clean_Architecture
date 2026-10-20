using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using AppTemplate.Infrastructure.Storage.Common.Budgets;
using AppTemplate.Infrastructure.Storage.Common.Options;

namespace AppTemplate.Infrastructure.Storage.Common.Factories;

internal static class BucketClientFactory
{
    internal const string SigningClientKey = "AppTemplate.Storage.Signing";

    internal static IAmazonS3 Create(FilesStorageS3Options options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return ClientFor(options, options.Endpoint);
    }

    internal static IAmazonS3 CreateForSigning(FilesStorageS3Options options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return ClientFor(options, options.SigningEndpoint);
    }

    private static AmazonS3Client ClientFor(FilesStorageS3Options options, string endpoint)
    {
        var configuration = new AmazonS3Config
        {
            ForcePathStyle = options.ForcePathStyle,
            Timeout = BucketBudget.AttemptTimeout,
            MaxErrorRetry = BucketBudget.MaxRetryAttempts,

            RetryMode = RequestRetryMode.Standard,
        };

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            configuration.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
        }
        else
        {
            configuration.ServiceURL = endpoint;
            configuration.AuthenticationRegion = options.Region;
        }

        return string.IsNullOrWhiteSpace(options.AccessKeyId)
            ? new AmazonS3Client(configuration)
            : new AmazonS3Client(
                new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey),
                configuration);
    }
}

using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using AppTemplate.Infrastructure.Storage.Common.Budgets;
using AppTemplate.Infrastructure.Storage.Common.Options;

namespace AppTemplate.Infrastructure.Storage.Common.Factories;

/// <summary>
/// Builds the S3 clients from validated options, in one place because endpoint, signing region,
/// address style and budget must agree or the failure is a rejected signature naming none of them.
/// <para>
/// The two clients cost one: <see cref="Create"/>'s is a process-wide singleton owning the connection
/// pool and retry schedule, while <see cref="CreateForSigning"/>'s only does Signature Version 4
/// arithmetic for a different host name and opens no socket.
/// </para>
/// </summary>
internal static class BucketClientFactory
{
    /// <summary>The key the presigning client is registered under; the clients differ by endpoint alone.</summary>
    internal const string SigningClientKey = "AppTemplate.Storage.Signing";

    /// <summary>
    /// The client for the calls this process makes itself. <paramref name="options"/> must already have
    /// passed <see cref="FilesStorageS3OptionsValidator"/>.
    /// </summary>
    internal static IAmazonS3 Create(FilesStorageS3Options options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return ClientFor(options, options.Endpoint);
    }

    /// <summary>
    /// The client every signed URL is minted by, built on
    /// <see cref="FilesStorageS3Options.SigningEndpoint"/>.
    /// </summary>
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

            // Standard: exponential backoff with jitter, which matters when replicas fail together.
            RetryMode = RequestRetryMode.Standard,
        };

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            configuration.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
        }
        else
        {
            // A named endpoint carries no region, and the region is part of the signing key.
            configuration.ServiceURL = endpoint;
            configuration.AuthenticationRegion = options.Region;
        }

        // No static credentials is the deployed case: the SDK's chain resolves an instance role.
        return string.IsNullOrWhiteSpace(options.AccessKeyId)
            ? new AmazonS3Client(configuration)
            : new AmazonS3Client(
                new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey),
                configuration);
    }
}

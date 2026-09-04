using Amazon;
using Amazon.Runtime;
using Amazon.S3;

namespace AppTemplate.Infrastructure.Storage.Buckets;

/// <summary>
/// Builds the S3 clients this process uses, from validated options.
/// <para>
/// It is a single place on purpose. The settings below — the endpoint, the signing region, the
/// address style, the budget — are meaningless individually: a compatible store needs all four to
/// agree or the failure is a rejected signature that names none of them.
/// </para>
/// <para>
/// <b>There are two clients and they cost one.</b> The rule that a client owns a connection pool and
/// a retry schedule, so a second one silently doubles both, is still true — and it is why
/// <see cref="Create"/>'s client is a process-wide singleton. It does not reach
/// <see cref="CreateForSigning"/>, because presigning opens nothing: Signature Version 4 is a keyed
/// hash of a string this process assembles from the request, and <c>GetPreSignedURLAsync</c> returns
/// without a socket ever being created. The second client is that arithmetic configured for a
/// different host name, which is the one thing a signature cannot be corrected for afterwards.
/// <c>BucketClientFactoryTests.CreateForSigning_OpensNoConnectionToTheEndpointItSignsFor</c> is what
/// keeps that from being a claim.
/// </para>
/// </summary>
internal static class BucketClientFactory
{
    /// <summary>
    /// The key the presigning client is registered under. Two <see cref="IAmazonS3"/> registrations
    /// differ by endpoint and by nothing else, so one of them has to be asked for by name.
    /// </summary>
    internal const string SigningClientKey = "AppTemplate.Storage.Signing";

    /// <summary>
    /// The client for the calls this process makes itself — a metadata read, a delete, a listing,
    /// and the inspector reading an object it is about to scan.
    /// <para>
    /// <paramref name="options"/> must already have passed <see cref="StorageOptionsValidator"/>;
    /// this reads them rather than checking them again.
    /// </para>
    /// </summary>
    internal static IAmazonS3 Create(StorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return ClientFor(options, options.Endpoint);
    }

    /// <summary>
    /// The client every signed URL is minted by, built on <see cref="StorageOptions.SigningEndpoint"/>
    /// — which is <see cref="StorageOptions.PublicEndpoint"/> where a deployment states one and
    /// <see cref="StorageOptions.Endpoint"/> where it does not, so a configuration that names no
    /// public endpoint gets exactly the client it got before this one existed.
    /// </summary>
    internal static IAmazonS3 CreateForSigning(StorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return ClientFor(options, options.SigningEndpoint);
    }

    private static AmazonS3Client ClientFor(StorageOptions options, string endpoint)
    {
        var configuration = new AmazonS3Config
        {
            ForcePathStyle = options.ForcePathStyle,
            Timeout = BucketBudget.AttemptTimeout,
            MaxErrorRetry = BucketBudget.MaxRetryAttempts,

            // Standard rather than Legacy: it is the mode with exponential backoff and jitter, which
            // is the half of the outbound policy that matters when several replicas fail against the
            // same store at the same moment.
            RetryMode = RequestRetryMode.Standard,
        };

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            configuration.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
        }
        else
        {
            // A named endpoint carries no region, and the region is part of the signing key — so it
            // has to be stated separately or every request is signed for a region the store did not
            // expect.
            configuration.ServiceURL = endpoint;
            configuration.AuthenticationRegion = options.Region;
        }

        // No static credentials is the deployed case, not a degenerate one: the SDK's own chain
        // resolves an instance role, and nothing about this process then holds a long-lived secret.
        return string.IsNullOrWhiteSpace(options.AccessKeyId)
            ? new AmazonS3Client(configuration)
            : new AmazonS3Client(
                new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey),
                configuration);
    }
}

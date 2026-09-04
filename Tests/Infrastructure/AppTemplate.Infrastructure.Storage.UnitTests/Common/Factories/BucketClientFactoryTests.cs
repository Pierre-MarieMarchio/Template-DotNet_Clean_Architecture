using System.Net;
using System.Net.Sockets;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using AppTemplate.Infrastructure.Storage.Common.Factories;
using AppTemplate.Infrastructure.Storage.UnitTests.Fixtures;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Storage.UnitTests.Common.Factories;

/// <summary>
/// The budget the SDK is built with, asserted because nothing else can see it.
/// <para>
/// The hosts install one outbound HTTP policy on <c>IHttpClientFactory</c>'s defaults and every typed
/// client inherits it without asking. The AWS SDK builds its own <c>HttpClient</c> and never meets
/// the factory, so it inherits nothing — and a call to the object store with no timeout and no retry
/// bound would look exactly like a working one until the day the store is slow. These numbers are
/// the policy's, restated, and this is what stops them from drifting apart silently.
/// </para>
/// </summary>
public sealed class BucketClientFactoryTests
{
    [Fact]
    public void Create_GivesTheSdkTheSameAttemptTimeoutAndRetryBudgetAsTheOutboundPolicy()
    {
        using var client = BucketClientFactory.Create(StorageFixture.Options());

        var configuration = client.Config;

        configuration.Timeout.ShouldBe(TimeSpan.FromSeconds(10));
        configuration.MaxErrorRetry.ShouldBe(3);
        configuration.RetryMode.ShouldBe(RequestRetryMode.Standard);
    }

    /// <summary>
    /// A named endpoint carries no region, and the region is half the signing key — so it has to be
    /// stated separately or every request is signed for one the store did not expect, which presents
    /// as a rejected signature and mentions no region at all.
    /// </summary>
    [Fact]
    public void Create_SignsForTheConfiguredRegionWhenTheEndpointIsACompatibleStore()
    {
        using var client = BucketClientFactory.Create(StorageFixture.Options(storage =>
        {
            storage.Endpoint = "https://objects.example";
            storage.ForcePathStyle = true;
        }));

        var configuration = (AmazonS3Config)client.Config;

        // The SDK normalises the endpoint it was given by appending a trailing slash.
        configuration.ServiceURL.ShouldBe("https://objects.example/");
        configuration.AuthenticationRegion.ShouldBe(StorageFixture.Region);
        configuration.ForcePathStyle.ShouldBeTrue();
    }

    [Fact]
    public void Create_ResolvesAwsOwnEndpointWhenNoneIsConfigured()
    {
        using var client = BucketClientFactory.Create(StorageFixture.Options());

        client.Config.RegionEndpoint.ShouldNotBeNull().SystemName.ShouldBe(StorageFixture.Region);
    }

    /// <summary>
    /// Static credentials are the exception, not the rule: a deployment with an instance role leaves
    /// both keys empty and the SDK's own chain supplies short-lived ones. What is asserted is that
    /// the two paths build a client at all — which credentials were resolved is the chain's business
    /// and is not observable here.
    /// </summary>
    [Fact]
    public void Create_BuildsAClientFromStaticCredentialsWhenTheyAreConfigured()
    {
        using var client = BucketClientFactory.Create(StorageFixture.Options());

        client.ShouldBeOfType<AmazonS3Client>();
    }

    [Fact]
    public void Create_RefusesToBuildAClientFromNothing()
    {
        Should.Throw<ArgumentNullException>(() => BucketClientFactory.Create(options: null!));
    }

    [Fact]
    public void CreateForSigning_RefusesToBuildAClientFromNothing()
    {
        Should.Throw<ArgumentNullException>(() => BucketClientFactory.CreateForSigning(options: null!));
    }

    /// <summary>
    /// The presigning client is built on the name a client outside this process can resolve, and the
    /// other one on the name this process reaches the store by. They differ in every self-hosted
    /// deployment, and a signature covers the host, so getting this wrong cannot be corrected
    /// anywhere downstream.
    /// </summary>
    [Fact]
    public void CreateForSigning_BuildsOnThePublicEndpointWhileCreateBuildsOnTheInternalOne()
    {
        var options = StorageFixture.Options(storage =>
        {
            storage.Endpoint = "http://minio:9000";
            storage.PublicEndpoint = "https://files.example";
        });

        using var talking = BucketClientFactory.Create(options);
        using var signing = BucketClientFactory.CreateForSigning(options);

        ((AmazonS3Config)talking.Config).ServiceURL.ShouldBe("http://minio:9000/");
        ((AmazonS3Config)signing.Config).ServiceURL.ShouldBe("https://files.example/");
    }

    /// <summary>
    /// A configuration that never heard of <c>Storage:PublicEndpoint</c> signs for exactly what it
    /// signed for before the setting existed. This is the whole of the compatibility promise at the
    /// factory: the two clients are then built from the same endpoint.
    /// </summary>
    [Theory]
    [InlineData("https://objects.example")]
    [InlineData("")]
    public void CreateForSigning_SignsForTheEndpointItselfWhenNoPublicOneIsStated(string endpoint)
    {
        var options = StorageFixture.Options(storage => storage.Endpoint = endpoint);

        using var talking = BucketClientFactory.Create(options);
        using var signing = BucketClientFactory.CreateForSigning(options);

        ((AmazonS3Config)signing.Config).ServiceURL.ShouldBe(((AmazonS3Config)talking.Config).ServiceURL);
        signing.Config.RegionEndpoint?.SystemName.ShouldBe(talking.Config.RegionEndpoint?.SystemName);
        signing.Config.AuthenticationRegion.ShouldBe(talking.Config.AuthenticationRegion);
    }

    /// <summary>
    /// <b>Why a second client costs nothing.</b> A presigned URL is a keyed hash of a string this
    /// process assembles locally; no request is sent and no socket is opened, so the second client
    /// doubles neither the connection pool nor the retry schedule that make a second client a defect
    /// everywhere else in this module.
    /// </summary>
    /// <remarks>
    /// Measured rather than asserted from the SDK's documentation: the endpoint below is a listening
    /// socket on this machine that accepts nothing, and what is checked is that nothing ever arrived
    /// at it. A store is not needed and none is running — a client that did connect would show up
    /// here as a pending connection, whatever answered it.
    /// </remarks>
    [Fact]
    public async Task CreateForSigning_OpensNoConnectionToTheEndpointItSignsFor()
    {
        var listener = new TcpListener(IPAddress.Loopback, port: 0);
        listener.Start();

        try
        {
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;

            var options = StorageFixture.Options(storage =>
            {
                storage.PublicEndpoint = $"http://127.0.0.1:{port}";
                storage.ForcePathStyle = true;
            });

            using var signing = BucketClientFactory.CreateForSigning(options);

            for (var minted = 0; minted < 5; minted++)
            {
                string url = await signing.GetPreSignedURLAsync(new GetPreSignedUrlRequest
                {
                    BucketName = StorageFixture.Bucket,
                    Key = StorageFixture.ObjectKey,
                    Verb = HttpVerb.GET,
                    Protocol = Protocol.HTTP,
                    Expires = DateTime.UtcNow.AddMinutes(5),
                });

                url.ShouldContain($"127.0.0.1:{port}");
            }

            listener.Pending().ShouldBeFalse(
                "presigning reached the endpoint. The second client is only affordable because it " +
                "opens nothing — if it connects, it owns a pool and a retry schedule like any other " +
                "and the design in BucketClientFactory's remarks does not hold.");
        }
        finally
        {
            listener.Stop();
            listener.Dispose();
        }
    }
}

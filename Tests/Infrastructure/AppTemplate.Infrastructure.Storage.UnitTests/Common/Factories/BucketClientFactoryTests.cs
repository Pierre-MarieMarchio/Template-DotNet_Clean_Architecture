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

    [Fact]
    public void Create_SignsForTheConfiguredRegionWhenTheEndpointIsACompatibleStore()
    {
        using var client = BucketClientFactory.Create(StorageFixture.Options(storage =>
        {
            storage.Endpoint = "https://objects.example";
            storage.ForcePathStyle = true;
        }));

        var configuration = (AmazonS3Config)client.Config;

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

    [Fact]
    public void Create_BuildsAClientFromStaticCredentialsWhenTheyAreConfigured()
    {
        using var client = BucketClientFactory.Create(StorageFixture.Options());

        client.ShouldBeOfType<AmazonS3Client>();
    }

    [Fact]
    public void Create_RefusesToBuildAClientFromNothing() =>
        Should.Throw<ArgumentNullException>(() => BucketClientFactory.Create(options: null!));

    [Fact]
    public void CreateForSigning_RefusesToBuildAClientFromNothing() =>
        Should.Throw<ArgumentNullException>(() => BucketClientFactory.CreateForSigning(options: null!));

    [Fact]
    public void CreateForSigning_BuildsOnThePublicEndpointWhileCreateBuildsOnTheInternalOne()
    {
        var options = StorageFixture.Options(storage =>
        {
            storage.Endpoint = "http://objectstore:9000";
            storage.PublicEndpoint = "https://files.example";
        });

        using var talking = BucketClientFactory.Create(options);
        using var signing = BucketClientFactory.CreateForSigning(options);

        ((AmazonS3Config)talking.Config).ServiceURL.ShouldBe("http://objectstore:9000/");
        ((AmazonS3Config)signing.Config).ServiceURL.ShouldBe("https://files.example/");
    }

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

            for (int minted = 0; minted < 5; minted++)
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

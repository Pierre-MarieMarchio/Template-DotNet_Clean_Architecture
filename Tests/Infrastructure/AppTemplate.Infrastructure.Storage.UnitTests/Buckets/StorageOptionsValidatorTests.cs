using AppTemplate.Infrastructure.Storage.Buckets;
using AppTemplate.Infrastructure.Storage.UnitTests.Fixtures;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Infrastructure.Storage.UnitTests.Buckets;

/// <summary>
/// What stops the process from booting. This validator runs under <c>ValidateOnStart</c>, so every
/// rule here is the difference between a misconfiguration reported at deployment and one reported by
/// a client, hours later, as a failed upload.
/// <para>
/// The plaintext-endpoint rule is the one worth the most. A signed URL is a bearer right to read or
/// write one object, and an <c>http://</c> endpoint means every one of them travels in clear — which
/// nothing anywhere else would report, because the upload works.
/// </para>
/// </summary>
public sealed class StorageOptionsValidatorTests
{
    private readonly StorageOptionsValidator _validator = new();

    [Fact]
    public void ADeployedConfiguration_IsAccepted()
    {
        Validate(StorageFixture.Options()).Succeeded.ShouldBeTrue();
    }

    /// <summary>
    /// No credentials at all is the shape a deployment with an instance role has: the SDK's own
    /// chain supplies short-lived ones and nothing here holds a long-lived secret.
    /// </summary>
    [Fact]
    public void AConfigurationWithNoCredentials_IsAccepted()
    {
        var options = StorageFixture.Options(storage =>
        {
            storage.AccessKeyId = string.Empty;
            storage.SecretAccessKey = string.Empty;
        });

        Validate(options).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABucketWithNoName_IsRefused(string bucketName)
    {
        Failures(storage => storage.BucketName = bucketName).ShouldContain(failure => failure.Contains("BucketName"));
    }

    /// <summary>
    /// Every one of these is refused by S3 itself, so accepting it here only moves the failure to the
    /// first request — where it arrives as a signature error or a redirect that mentions none of it.
    /// </summary>
    [Theory]
    [InlineData("ab")]
    [InlineData("App-Files")]
    [InlineData("app_files")]
    [InlineData("-app-files")]
    [InlineData("app-files-")]
    public void AMalformedBucketName_IsRefused(string bucketName)
    {
        Failures(storage => storage.BucketName = bucketName).ShouldContain(failure => failure.Contains("BucketName"));
    }

    [Fact]
    public void ARegionThatIsNotStated_IsRefused()
    {
        Failures(storage => storage.Region = " ").ShouldContain(failure => failure.Contains("Region"));
    }

    [Theory]
    [InlineData("minio:9000")]
    [InlineData("ftp://objects.example")]
    [InlineData("/objects")]
    public void AnEndpointThatIsNotAnHttpUrl_IsRefused(string endpoint)
    {
        Failures(storage => storage.Endpoint = endpoint).ShouldContain(failure => failure.Contains("Endpoint"));
    }

    [Fact]
    public void APlaintextEndpointAgainstAHostThatIsNotLoopback_IsRefused()
    {
        Failures(storage => storage.Endpoint = "http://minio:9000")
            .ShouldContain(failure => failure.Contains("clear text"));
    }

    [Fact]
    public void APlaintextEndpointAgainstLoopback_IsAccepted()
    {
        Validate(StorageFixture.Options(storage => storage.Endpoint = "http://localhost:9000"))
            .Succeeded.ShouldBeTrue();
    }

    /// <summary>
    /// The escape hatch, and it is deliberate rather than reachable by picking a permissive value
    /// somewhere else — the same shape <c>EmailOptions.AllowInsecureTransport</c> has, for the same
    /// containerised-development reason.
    /// </summary>
    [Fact]
    public void APlaintextEndpointAcceptedDeliberately_IsAllowed()
    {
        var options = StorageFixture.Options(storage =>
        {
            storage.Endpoint = "http://minio:9000";
            storage.AllowInsecureTransport = true;
        });

        Validate(options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void AnHttpsEndpoint_NeedsNoPermission()
    {
        Validate(StorageFixture.Options(storage => storage.Endpoint = "https://objects.example"))
            .Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("minio:9000")]
    [InlineData("ftp://objects.example")]
    [InlineData("/objects")]
    public void APublicEndpointThatIsNotAnHttpUrl_IsRefused(string publicEndpoint)
    {
        Failures(storage => storage.PublicEndpoint = publicEndpoint)
            .ShouldContain(failure => failure.Contains("PublicEndpoint"));
    }

    /// <summary>
    /// The rule that matters, aimed where it belongs. A signed URL is a bearer right to read or write
    /// one object, and the endpoint it is minted for is the one it travels to — so a plaintext public
    /// endpoint is the case worth refusing, whatever the endpoint this process talks to happens to be.
    /// </summary>
    [Fact]
    public void APlaintextPublicEndpointBehindAnEncryptedInternalOne_IsRefused()
    {
        var failures = Failures(storage =>
        {
            storage.Endpoint = "https://minio.internal";
            storage.PublicEndpoint = "http://files.example";
        });

        failures.ShouldContain(failure => failure.Contains("clear text"));
        failures.ShouldContain(failure => failure.Contains($"'{StorageOptions.SectionName}:PublicEndpoint'"));
    }

    /// <summary>
    /// <b>The deployment the old rule had backwards.</b> Plain HTTP inside a service mesh with TLS at
    /// the ingress is the ordinary shape, and it used to be forced to declare
    /// <c>AllowInsecureTransport</c> — which then let a genuinely plaintext public endpoint through
    /// unnoticed. The internal endpoint carries this process's own calls, signed per request, inside a
    /// network the operator chose; nothing re-usable is minted from it.
    /// </summary>
    [Fact]
    public void APlaintextInternalEndpointUnderAnEncryptedPublicOne_NeedsNoPermission()
    {
        var options = StorageFixture.Options(storage =>
        {
            storage.Endpoint = "http://minio:9000";
            storage.PublicEndpoint = "https://files.example";
        });

        Validate(options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void APlaintextPublicEndpointAcceptedDeliberately_IsAllowed()
    {
        var options = StorageFixture.Options(storage =>
        {
            storage.Endpoint = "https://minio.internal";
            storage.PublicEndpoint = "http://files.example";
            storage.AllowInsecureTransport = true;
        });

        Validate(options).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void APlaintextPublicEndpointAgainstLoopback_IsAccepted()
    {
        var options = StorageFixture.Options(storage =>
        {
            storage.Endpoint = "http://minio:9000";
            storage.PublicEndpoint = "http://localhost:9000";
        });

        Validate(options).Succeeded.ShouldBeTrue();
    }

    /// <summary>
    /// The failure names the key the operator actually wrote. A deployment that never stated a public
    /// endpoint has no such line to correct, and sending it looking for one is how a clear message
    /// becomes a support thread.
    /// </summary>
    [Fact]
    public void TheClearTextFailure_NamesTheKeyThatCarriesTheEndpointItIsAbout()
    {
        Failures(storage => storage.Endpoint = "http://minio:9000")
            .ShouldContain(failure => failure.Contains($"'{StorageOptions.SectionName}:Endpoint' is an http URL"));

        Failures(storage =>
            {
                storage.Endpoint = "http://minio:9000";
                storage.PublicEndpoint = "http://files.example";
            })
            .ShouldContain(failure =>
                failure.Contains($"'{StorageOptions.SectionName}:PublicEndpoint' is an http URL"));
    }

    /// <summary>
    /// A configuration written before <c>Storage:PublicEndpoint</c> existed gets the same verdict it
    /// always got, because the endpoint the rule looks at defaults to the one such a configuration
    /// states. That is the compatibility promise, and it is a rule rather than a coincidence.
    /// </summary>
    [Theory]
    [InlineData("http://minio:9000", false, false)]
    [InlineData("http://minio:9000", true, true)]
    [InlineData("http://localhost:9000", false, true)]
    [InlineData("https://objects.example", false, true)]
    [InlineData("", false, true)]
    public void AConfigurationWithNoPublicEndpoint_IsJudgedExactlyAsItWasBefore(
        string endpoint,
        bool allowInsecure,
        bool expected)
    {
        var options = StorageFixture.Options(storage =>
        {
            storage.Endpoint = endpoint;
            storage.AllowInsecureTransport = allowInsecure;
        });

        options.PublicEndpoint.ShouldBe(
            string.Empty,
            "the default has to be absent, or every deployment that predates the setting acquires one.");

        Validate(options).Succeeded.ShouldBe(expected);
    }

    /// <summary>
    /// Half a credential pair is always a mistake, and the shape it takes is a process that starts
    /// and signs every request as nobody.
    /// </summary>
    [Fact]
    public void AKeyIdWithNoSecret_IsRefused()
    {
        Failures(storage => storage.SecretAccessKey = string.Empty)
            .ShouldContain(failure => failure.Contains("SecretAccessKey"));
    }

    [Fact]
    public void ASecretWithNoKeyId_IsRefused()
    {
        Failures(storage => storage.AccessKeyId = string.Empty)
            .ShouldContain(failure => failure.Contains("AccessKeyId"));
    }

    [Fact]
    public void AGrantCeilingOfZero_IsRefused()
    {
        Failures(storage => storage.MaxGrantLifetime = TimeSpan.Zero)
            .ShouldContain(failure => failure.Contains("MaxGrantLifetime"));
    }

    /// <summary>
    /// Signature Version 4 will not sign for longer than seven days, so a larger ceiling could never
    /// be honoured and would present as a signing exception on somebody's first upload.
    /// </summary>
    [Fact]
    public void AGrantCeilingLongerThanASignatureCanCarry_IsRefused()
    {
        Failures(storage => storage.MaxGrantLifetime = StorageOptions.MaxSignableLifetime + TimeSpan.FromSeconds(1))
            .ShouldContain(failure => failure.Contains("MaxGrantLifetime"));
    }

    [Fact]
    public void EveryFailure_NamesItsConfigurationKeyInFull()
    {
        var options = StorageFixture.Options(storage =>
        {
            storage.BucketName = string.Empty;
            storage.Region = string.Empty;
        });

        Validate(options).Failures.ShouldNotBeNull().ShouldAllBe(failure => failure.Contains($"'{StorageOptions.SectionName}:"));
    }

    private IEnumerable<string> Failures(Action<StorageOptions> adjust)
    {
        var result = Validate(StorageFixture.Options(adjust));

        result.Failed.ShouldBeTrue();

        return result.Failures ?? [];
    }

    private ValidateOptionsResult Validate(StorageOptions options) =>
        _validator.Validate(name: null, options);
}

using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using AppTemplate.Api.Common.Errors;
using AppTemplate.Api.Common.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.UnitTests.Common.Security;

public sealed class RateLimitingExtensionsTests
{
    private static readonly TimeSpan _anHour = TimeSpan.FromHours(1);

    [Fact]
    public void TheGlobalLimiter_AdmitsTheConfiguredPermits_ThenRefuses()
    {
        using var provider = BuildProvider(("GlobalPermitLimit", "3"), ("Window", "01:00:00"));
        using var globalLimiter = GlobalLimiterOf(provider);
        var caller = CreateHttpContext("203.0.113.7");

        for (int request = 1; request <= 3; request++)
        {
            using var allowed = globalLimiter.AttemptAcquire(caller);
            allowed.IsAcquired.ShouldBeTrue($"request {request} of 3 is inside the budget");
        }

        using var refused = globalLimiter.AttemptAcquire(caller);

        refused.IsAcquired.ShouldBeFalse();
    }

    [Fact]
    public void WithNothingConfigured_TheGlobalLimiterAdmitsTheDefaultPermits()
    {
        var defaults = new RateLimitingOptions();

        using var provider = BuildProvider(("Window", "01:00:00"));
        using var globalLimiter = GlobalLimiterOf(provider);
        var caller = CreateHttpContext("203.0.113.7");

        for (int request = 1; request <= defaults.GlobalPermitLimit; request++)
        {
            using var allowed = globalLimiter.AttemptAcquire(caller);
            allowed.IsAcquired.ShouldBeTrue($"request {request} is inside the default budget");
        }

        using var refused = globalLimiter.AttemptAcquire(caller);

        refused.IsAcquired.ShouldBeFalse();
    }

    [Fact]
    public void TheDefaults_AreThePublishedLimits()
    {
        var defaults = new RateLimitingOptions();

        //#if (localAuth)
        defaults.AuthenticationPermitLimit.ShouldBe(10);
        //#endif
        defaults.GlobalPermitLimit.ShouldBe(300);
        defaults.Window.ShouldBe(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void ExhaustingOneAddressesBudget_LeavesAnotherAddressUnaffected()
    {
        using var limiter = PartitionedRateLimiter.Create(RateLimitingExtensions.FixedWindowPerAddress(2, _anHour));
        var exhausted = CreateHttpContext("203.0.113.7");
        var other = CreateHttpContext("203.0.113.8");

        for (int request = 1; request <= 2; request++)
        {
            using var allowed = limiter.AttemptAcquire(exhausted);
            allowed.IsAcquired.ShouldBeTrue();
        }

        using var refused = limiter.AttemptAcquire(exhausted);
        refused.IsAcquired.ShouldBeFalse();

        using var fromTheOtherCaller = limiter.AttemptAcquire(other);
        fromTheOtherCaller.IsAcquired.ShouldBeTrue();
    }

    [Fact]
    public void ARefusal_CarriesRetryAfter()
    {
        using var limiter = PartitionedRateLimiter.Create(RateLimitingExtensions.FixedWindowPerAddress(1, _anHour));
        var caller = CreateHttpContext("203.0.113.7");

        using var allowed = limiter.AttemptAcquire(caller);
        allowed.IsAcquired.ShouldBeTrue();

        using var refused = limiter.AttemptAcquire(caller);

        refused.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter).ShouldBeTrue();
        retryAfter.ShouldBeGreaterThan(TimeSpan.Zero);
        retryAfter.ShouldBeLessThanOrEqualTo(_anHour);
    }

    [Fact]
    public async Task ACallerOverBudget_IsRefusedImmediatelyRatherThanQueued()
    {
        using var limiter = PartitionedRateLimiter.Create(RateLimitingExtensions.FixedWindowPerAddress(1, _anHour));
        var caller = CreateHttpContext("203.0.113.7");
        var cancellationToken = TestContext.Current.CancellationToken;

        using var allowed = await limiter.AcquireAsync(caller, cancellationToken: cancellationToken);
        allowed.IsAcquired.ShouldBeTrue();

        using var refused = await limiter.AcquireAsync(caller, cancellationToken: cancellationToken);

        refused.IsAcquired.ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("application/json")]
    [InlineData("text/html")]
    [InlineData("application/xml")]
    public async Task TheRejection_AnswersTheSameProblemWhateverTheAcceptHeaderSays(string? accept)
    {
        using var provider = BuildProvider();
        var options = OptionsOf(provider);

        var httpContext = CreateHttpContext("203.0.113.7");
        httpContext.RequestServices = provider;
        httpContext.TraceIdentifier = "trace-1";
        httpContext.Response.Body = new MemoryStream();

        if (accept is not null)
        {
            httpContext.Request.Headers.Accept = accept;
        }

        using var limiter = PartitionedRateLimiter.Create(RateLimitingExtensions.FixedWindowPerAddress(1, _anHour));
        using var allowed = limiter.AttemptAcquire(httpContext);
        using var refused = limiter.AttemptAcquire(httpContext);

        await options.OnRejected!(
            new OnRejectedContext { HttpContext = httpContext, Lease = refused },
            TestContext.Current.CancellationToken);

        httpContext.Response.ContentType.ShouldStartWith("application/problem+json");
        httpContext.Response.Headers.RetryAfter.ToString().ShouldNotBeNullOrEmpty();

        httpContext.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(
            httpContext.Response.Body,
            cancellationToken: TestContext.Current.CancellationToken);
        var root = body.RootElement;

        root.GetProperty("status").GetInt32().ShouldBe(429);
        root.GetProperty("title").GetString().ShouldBe("Too many requests");
        root.GetProperty("code").GetString().ShouldBe("rateLimit.exceeded");
        root.GetProperty("traceId").GetString().ShouldBe("trace-1");
        root.GetProperty("type").GetString().ShouldEndWith("/rateLimit.exceeded");
        options.RejectionStatusCode.ShouldBe(StatusCodes.Status429TooManyRequests);
    }

    private static ServiceProvider BuildProvider(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(
                setting => $"{RateLimitingOptions.SectionName}:{setting.Key}",
                setting => (string?)setting.Value))
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddApiProblemDetails();
        services.AddApiRateLimiting(configuration);

        return services.BuildServiceProvider();
    }

    private static RateLimiterOptions OptionsOf(IServiceProvider provider) =>
        provider.GetRequiredService<IOptions<RateLimiterOptions>>().Value;

    private static PartitionedRateLimiter<HttpContext> GlobalLimiterOf(IServiceProvider provider)
    {
        var globalLimiter = OptionsOf(provider).GlobalLimiter;

        globalLimiter.ShouldNotBeNull("AddApiRateLimiting installs a global limiter.");

        return globalLimiter;
    }

    private static DefaultHttpContext CreateHttpContext(string address) =>
        new() { Connection = { RemoteIpAddress = IPAddress.Parse(address) } };
}

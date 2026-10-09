using System.Net;
using System.Text.Json;
using System.Threading.RateLimiting;
using AppTemplate.Api.Core.Common.Errors;
using AppTemplate.Api.Core.Common.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AppTemplate.Api.Core.UnitTests.Common.Security;

/// <summary>
/// What <c>AddApiRateLimiting</c> composes, read from the options the middleware would be handed and
/// exercised through the same <c>PartitionedRateLimiter</c> the middleware drives.
/// </summary>
/// <remarks>
/// <c>RateLimiterOptions</c> keeps its named policies in an internal map, so the <c>authentication</c>
/// policy cannot be invoked from here. Both policies are built by the same helper, which is tested
/// directly; what the policy does with it afterwards is the integration suite's.
/// </remarks>
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

        defaults.AuthenticationPermitLimit.ShouldBe(10);
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

    /// <summary>
    /// The refusal carries the metadata the rejection turns into the <c>Retry-After</c> header; a
    /// limiter that refused without it would still answer 429 and the documented header would vanish.
    /// </summary>
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

    /// <summary>
    /// A caller over budget is told so rather than parked: with a queue, the awaiting request would
    /// hold a thread for the rest of the window, the opposite of shedding load.
    /// </summary>
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

    /// <summary>
    /// The 429 is written by hand because <c>IProblemDetailsService</c> writes nothing when the
    /// request's <c>Accept</c> header excludes JSON. A browser-style header must still get the body,
    /// the content type, <c>Retry-After</c> and the members every other problem carries.
    /// </summary>
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

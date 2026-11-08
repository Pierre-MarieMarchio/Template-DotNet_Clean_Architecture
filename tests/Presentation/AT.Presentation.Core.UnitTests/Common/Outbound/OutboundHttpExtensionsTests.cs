using System.Net;
using AppTemplate.Presentation.Core.Common.Outbound;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Shouldly;
using Xunit;

namespace AppTemplate.Presentation.Core.UnitTests.Common.Outbound;

public sealed class OutboundHttpExtensionsTests
{
    private const string _defaultsOptionsName = "-standard";

    private const string _probeClient = "probe";

    [Fact]
    public void TheBudget_IsWrittenDownRatherThanInherited()
    {
        HttpStandardResilienceOptions options = Resolve(out var provider);

        using (provider)
        {
            options.AttemptTimeout.Timeout.ShouldBe(TimeSpan.FromSeconds(10));
            options.TotalRequestTimeout.Timeout.ShouldBe(TimeSpan.FromSeconds(30));

            options.Retry.MaxRetryAttempts.ShouldBe(3);
            options.Retry.BackoffType.ShouldBe(DelayBackoffType.Exponential);
            options.Retry.UseJitter.ShouldBeTrue();

            options.CircuitBreaker.SamplingDuration.ShouldBeGreaterThanOrEqualTo(
                options.AttemptTimeout.Timeout * 2);
        }
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    public async Task ATransientFailure_IsRetried_ForAVerbThatIsSafeToReplay(string verb) =>
        (await ShouldRetryAsync(verb)).ShouldBeTrue();

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("PROPFIND")]
    public async Task ATransientFailure_IsNotRetried_ForAnyOtherVerb(string verb) =>
        (await ShouldRetryAsync(verb)).ShouldBeFalse();

    [Fact]
    public async Task ATransientFailure_IsNotRetried_WhenTheVerbIsUnknown() =>
        (await ShouldRetryAsync(verb: null)).ShouldBeFalse();

    private static async Task<bool> ShouldRetryAsync(string? verb)
    {
        HttpStandardResilienceOptions options = Resolve(out var provider);

        using (provider)
        {
            ResilienceContext context = ResilienceContextPool.Shared.Get();

            try
            {
                if (verb is not null)
                {
                    context.SetRequestMessage(
                        new HttpRequestMessage(new HttpMethod(verb), "https://dependency.test/"));
                }

                using var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);

                return await options.Retry.ShouldHandle(
                    new RetryPredicateArguments<HttpResponseMessage>(
                        context,
                        Outcome.FromResult(response),
                        attemptNumber: 0));
            }
            finally
            {
                ResilienceContextPool.Shared.Return(context);
            }
        }
    }

    [Theory]
    [InlineData("GET", 4)]
    [InlineData("POST", 1)]
    public async Task TheHandlerInARealClient_RunsThatSamePredicate(string verb, int expected)
    {
        var dependency = new CountingHandler();

        var services = new ServiceCollection();
        services.AddHttpClient(_probeClient).ConfigurePrimaryHttpMessageHandler(() => dependency);
        services.AddOutboundHttp();

        services.Configure<HttpStandardResilienceOptions>(_defaultsOptionsName, options =>
        {
            options.Retry.Delay = TimeSpan.FromMilliseconds(1);
            options.Retry.UseJitter = false;
        });

        using var provider = services.BuildServiceProvider();

        HttpClient client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(_probeClient);

        using var request = new HttpRequestMessage(new HttpMethod(verb), "https://dependency.test/");
        using HttpResponseMessage response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(
            HttpStatusCode.InternalServerError,
            "the policy must surface the dependency's failure rather than swallow it");

        dependency.Attempts.ShouldBe(expected);
    }

    private static HttpStandardResilienceOptions Resolve(out ServiceProvider provider)
    {
        var services = new ServiceCollection();
        services.AddHttpClient(_probeClient);
        services.AddOutboundHttp();

        provider = services.BuildServiceProvider();

        return provider
            .GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>()
            .Get(_defaultsOptionsName);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        private int _attempts;

        internal int Attempts => Volatile.Read(ref _attempts);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _attempts);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }
}

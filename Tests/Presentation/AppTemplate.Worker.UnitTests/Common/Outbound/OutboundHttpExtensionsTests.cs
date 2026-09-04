using System.Net;
using AppTemplate.Worker.Common.Outbound;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using Shouldly;
using Xunit;

namespace AppTemplate.Worker.UnitTests.Common.Outbound;

/// <summary>
/// The verb allow-list this host wraps around the package's own idea of a transient failure, and the
/// budget it writes down.
/// <para>
/// <c>OutboundHttpTests</c> in the architecture project proves the host installs the policy and that
/// nothing constructs its own <c>HttpClient</c> around it; neither reads a value, so a <c>PUT</c>
/// becoming replayable — the thing the policy argues hardest for — would only be seen here.
/// AppTemplate.Api's twin file has no equivalent test either.
/// </para>
/// </summary>
public sealed class OutboundHttpExtensionsTests
{
    /// <summary>
    /// The options name the policy actually configures. It is <c>"-standard"</c>, not
    /// <c>"{client}-standard"</c>, because the policy goes on <c>ConfigureHttpClientDefaults</c> —
    /// the empty client name is the defaults', and every named client inherits from it.
    /// <para>
    /// <b>Getting this wrong reads as a pass.</b> A name nothing configured resolves to a
    /// default-constructed instance, and this package's defaults happen to be
    /// <em>identical</em> to the numbers below — 10 s, 30 s, three retries, exponential, jitter — so
    /// every assertion in <see cref="TheBudget_IsWrittenDownRatherThanInherited"/> passes against
    /// <c>"probe-standard"</c> with this host's policy removed entirely. The allow-list is what tells
    /// the two apart, since the package retries every verb.
    /// </para>
    /// </summary>
    private const string _defaultsOptionsName = "-standard";

    private const string _probeClient = "probe";

    [Fact]
    public void TheBudget_IsWrittenDownRatherThanInherited()
    {
        HttpStandardResilienceOptions options = Resolve(out var provider);

        using (provider)
        {
            // 10 s inside 30 s, and 30 s inside the API's five-minute request timeout by a factor of
            // ten. These coincide with the package's current defaults, which is the point of writing
            // them down: a dependency reached on a budget nobody wrote down is a dependency whose
            // budget changes when the package's does. If either moves in AppTemplate.Api's twin file
            // it moves here, and this catches one of the two moving alone.
            options.AttemptTimeout.Timeout.ShouldBe(TimeSpan.FromSeconds(10));
            options.TotalRequestTimeout.Timeout.ShouldBe(TimeSpan.FromSeconds(30));

            options.Retry.MaxRetryAttempts.ShouldBe(3);
            options.Retry.BackoffType.ShouldBe(DelayBackoffType.Exponential);
            options.Retry.UseJitter.ShouldBeTrue();

            // The package validates at start-up that the breaker's sampling window is at least twice
            // the attempt timeout, and 30 s against 10 s satisfies it with no margin at all. Asserted
            // so that raising the attempt timeout alone fails a test rather than a deployment.
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

    /// <summary>
    /// PUT and DELETE are refused even though the specification calls them idempotent: that promise
    /// belongs to the server at the other end, and a default applies to servers nobody here
    /// controls. <c>PROPFIND</c> is the case the package's own <c>DisableForUnsafeHttpMethods</c>
    /// gets wrong — it is a deny-list, so a verb it does not name stays retryable, which is why this
    /// policy writes an allow-list instead.
    /// </summary>
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    [InlineData("PROPFIND")]
    public async Task ATransientFailure_IsNotRetried_ForAnyOtherVerb(string verb) =>
        (await ShouldRetryAsync(verb)).ShouldBeFalse();

    /// <summary>
    /// A failed attempt frequently carries no request message: a timeout or a connection failure
    /// arrives as an exception, and the verb is read from the resilience context rather than from a
    /// response that may not exist. The allow-list has to refuse rather than assume.
    /// </summary>
    [Fact]
    public async Task ATransientFailure_IsNotRetried_WhenTheVerbIsUnknown() =>
        (await ShouldRetryAsync(verb: null)).ShouldBeFalse();

    /// <summary>
    /// The predicate itself, which is where the allow-list lives. A 500 is what the package
    /// classifies as transient, so a <c>false</c> here can only have come from the verb.
    /// </summary>
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

    /// <summary>
    /// Proves the predicate the tests above read is the one the handler in a real client runs, which
    /// reading options cannot establish on its own: a policy configured and never installed would
    /// answer every question above correctly and retry nothing. Two verbs are enough — the retried
    /// one and a refused one — because the cases are covered exhaustively above and each retried
    /// request here waits out the shipped exponential backoff.
    /// </summary>
    [Theory]
    [InlineData("GET", 4)]
    [InlineData("POST", 1)]
    public async Task TheHandlerInARealClient_RunsThatSamePredicate(string verb, int expected)
    {
        var dependency = new CountingHandler();

        var services = new ServiceCollection();
        services.AddHttpClient(_probeClient).ConfigurePrimaryHttpMessageHandler(() => dependency);
        services.AddWorkerOutboundHttp();

        // The only value overridden, and only so this finishes: the shipped two-second exponential
        // base would make the retried case wait fourteen seconds. The retry count and the allow-list
        // are the host's own, which is what is under test.
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
        services.AddWorkerOutboundHttp();

        provider = services.BuildServiceProvider();

        return provider
            .GetRequiredService<IOptionsMonitor<HttpStandardResilienceOptions>>()
            .Get(_defaultsOptionsName);
    }

    /// <summary>Answers every request with a 500 — what the package classifies as transient — and
    /// counts how many it was asked.</summary>
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

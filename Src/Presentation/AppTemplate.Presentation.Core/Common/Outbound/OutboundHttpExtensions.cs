using Microsoft.Extensions.DependencyInjection;
using Polly;

namespace AppTemplate.Presentation.Core.Common.Outbound;

/// <summary>
/// The one outbound HTTP policy, applied to every client a host's <c>IHttpClientFactory</c> hands
/// out, whichever module registered it.
/// <para>
/// It goes on the factory's defaults rather than into a method each module calls, because a default
/// needs no cooperation from the module and cannot be opted out of by accident.
/// </para>
/// <para>
/// One policy for every host, which is what makes it a policy at all. The modules that call
/// outwards — mail, identity — are composed by more than one host, and a budget enforced in one of
/// them only is worse than none, because the host that misses it is the one nobody watches.
/// </para>
/// </summary>
public static class OutboundHttpExtensions
{
    /// <summary>
    /// Installs the resilience handler on the HTTP client factory's defaults.
    /// </summary>
    /// <param name="services">The container being built.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddOutboundHttp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler(options =>
        {
            // 30 s total inside the API's 5-minute inbound timeout, so the outer budget stays the longer. The package
            // needs total > attempt timeout and a 30 s sampling window of at least twice it: 10 s / 30 s satisfies both,
            // with no margin on the second.
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);

            options.Retry.MaxRetryAttempts = 3;
            options.Retry.BackoffType = DelayBackoffType.Exponential;

            // Without jitter, replicas failing against the same dependency retry in the same
            // millisecond.
            options.Retry.UseJitter = true;

            // An allow-list of safe verbs, not the package's deny-list, which would retry any verb it does not name; PUT
            // and DELETE are out because replaying a large PUT doubles the bytes. The verb comes from the resilience
            // context, since a timeout arrives as an exception with no response.
            var isTransientFailure = options.Retry.ShouldHandle;

            options.Retry.ShouldHandle = args =>
                IsSafeToReplay(args.Context.GetRequestMessage()?.Method)
                    ? isTransientFailure(args)
                    : PredicateResult.False();

            // The circuit breaker and the concurrency limiter keep the package's defaults; the
            // limiter is what stops one slow dependency occupying every thread that calls it.
        }));

        return services;
    }

    private static bool IsSafeToReplay(HttpMethod? method) =>
        method is not null
        && (method == HttpMethod.Get
            || method == HttpMethod.Head
            || method == HttpMethod.Options
            || method == HttpMethod.Trace);
}

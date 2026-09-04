using Polly;

namespace AppTemplate.Api.Common.Outbound;

/// <summary>
/// The one outbound HTTP policy, applied to every client this host's
/// <c>IHttpClientFactory</c> hands out, whichever module registered it.
/// <para>
/// It goes on the factory's defaults rather than into a method each module calls, because a default
/// needs no cooperation from the module and cannot be opted out of by accident.
/// </para>
/// <para>
/// <c>Src/Presentation/AppTemplate.Worker/Common/Outbound/OutboundHttpExtensions.cs</c> is this
/// file's twin and the two policies have to stay identical: the modules that call outwards — Email
/// and Identity — are composed by both hosts, and a budget enforced in one host only is worse than
/// none, because the host that misses it is the one nobody thinks about. They are two files rather
/// than one because the worker deliberately does not reference <c>AppTemplate.Api</c>; a difference
/// between them is a bug, not a variation.
/// </para>
/// </summary>
internal static class OutboundHttpExtensions
{
    internal static IServiceCollection AddApiOutboundHttp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler(options =>
        {
            // An outbound call happens inside an inbound request, and
            // AppTemplate.Api.Common.Hosting.RequestTimeoutsOptions.Default gives that request
            // 5 minutes. The rule that type states about the layer below it applies again one
            // level up: the enclosing budget has to be the longer of the two, or the outer timeout
            // cancels work that was still retrying correctly underneath and reports the caller's
            // deadline instead of the dependency's failure. 30 s inside 300 s leaves a factor of
            // ten, so a request that calls several dependencies in turn still finishes inside its
            // own budget. If either number moves, re-check this ratio.
            //
            // Per attempt, 10 s: a dependency that has not answered in 10 s does not answer better
            // at 60, it just holds the caller longer.
            //
            // The package validates the combination at start-up, not at the first call: the total
            // timeout must exceed the attempt timeout, and the circuit breaker's sampling window
            // (30 s, left at the default below) must be at least twice the attempt timeout.
            // 10 s / 30 s satisfies both, with no margin on the second — doubling the attempt
            // timeout alone would fail the build's own start-up validation.
            options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
            options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);

            options.Retry.MaxRetryAttempts = 3;
            options.Retry.BackoffType = DelayBackoffType.Exponential;

            // Jitter, because several replicas failing against the same dependency otherwise retry
            // in the same millisecond, which is the worst moment to arrive together.
            options.Retry.UseJitter = true;

            // Retry is decided per verb, as an allow-list of the safe ones. The package ships
            // DisableForUnsafeHttpMethods(), which is the deny-list POST, PATCH, PUT, DELETE,
            // CONNECT — close, but not this rule: any verb it does not name (a WebDAV method, a
            // future one) would still be retried. A default that covers every client in the host
            // has to be the one that is never wrong; relaxing it for a client whose server is known
            // is one line, and discovering it was wrong costs an incident.
            //
            // PUT and DELETE are out even though the specification calls them idempotent, because
            // that promise belongs to the server at the other end and a default applies to servers
            // nobody here controls. Replaying a 200 MiB PUT because the response was slow doubles
            // the bytes, and against a server that does not keep the promise it writes twice.
            //
            // The verb is read from the resilience context, not from the outcome's response: a
            // failed attempt frequently has no response at all — a timeout or a connection failure
            // arrives as an exception — and an allow-list that could not see the verb there would
            // refuse exactly the retries this policy exists for. Wrapping the existing predicate
            // rather than replacing it keeps the package's own definition of a transient failure.
            var isTransientFailure = options.Retry.ShouldHandle;

            options.Retry.ShouldHandle = args =>
                IsSafeToReplay(args.Context.GetRequestMessage()?.Method)
                    ? isTransientFailure(args)
                    : PredicateResult.False();

            // The circuit breaker and the concurrency limiter keep the package's defaults. The
            // limiter is the part that matters most here: it is what stops one slow dependency
            // from occupying every thread that wanted to call it, and a number invented in this
            // file would be a number no one could justify later.
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

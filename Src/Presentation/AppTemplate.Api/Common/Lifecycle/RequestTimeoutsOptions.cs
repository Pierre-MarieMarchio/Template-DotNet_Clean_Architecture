using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Lifecycle;

/// <summary>
/// How long a request may run before it is cut loose.
/// <para>
/// Public because it is bound from configuration and its section name is part of the template's
/// contract with whoever deploys it.
/// </para>
/// </summary>
public sealed class RequestTimeoutsOptions
{
    public const string SectionName = "RequestTimeouts";

    /// <summary>
    /// 5 minutes by default, applied to every endpoint that does not name a different policy.
    /// <para>
    /// This has to reconcile with the layer underneath: persistence commits with a 30-second
    /// command timeout and retries a transient failure up to five times
    /// (<c>EnableRetryOnFailure</c>), so a single database call can legitimately occupy on the
    /// order of 230 seconds (up to six attempts of 30 seconds, spaced by up to five 10-second
    /// backoffs) before it gives up on its own. <b>This default must stay longer than that
    /// ceiling, not shorter.</b> A shorter request timeout would routinely cancel a write that is
    /// still safely retrying underneath — trading a transient failure the driver would have
    /// recovered from for a write whose outcome the caller can no longer observe, which is the one
    /// outcome a caller with no safe way to retry a side effect must never be handed. If the retry
    /// budget below is ever tightened, shrink this to match; it must never be the one that moves
    /// first.
    /// </para>
    /// </summary>
    public TimeSpan Default { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 10 minutes by default, reachable only through the
    /// <see cref="HostLifecycleExtensions.LongRequestTimeoutPolicy"/> named policy, for a future endpoint
    /// whose normal work — not a stream — legitimately runs longer than <see cref="Default"/> (a
    /// bulk import, say). It is still an ordinary request/response, so a timeout here can still
    /// answer a <c>ProblemDetails</c>.
    /// <para>
    /// A streaming endpoint (SSE, an <c>IAsyncEnumerable</c> response) must reach for
    /// <c>[DisableRequestTimeout]</c> instead of this policy, never a larger number here: once the
    /// first byte is flushed there is no channel left to report a timeout on, so a "very large"
    /// value is not a safer choice, just a later one — it is still reached eventually, at the
    /// worst possible moment, and produces a silent cutoff instead of a clean error either way.
    /// </para>
    /// </summary>
    public TimeSpan Extended { get; set; } = TimeSpan.FromMinutes(10);
}

internal sealed class RequestTimeoutsOptionsValidator : IValidateOptions<RequestTimeoutsOptions>
{
    private static readonly TimeSpan _minTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _maxTimeout = TimeSpan.FromHours(1);

    public ValidateOptionsResult Validate(string? name, RequestTimeoutsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.Default < _minTimeout || options.Default > _maxTimeout)
        {
            failures.Add(
                $"'{RequestTimeoutsOptions.SectionName}:{nameof(RequestTimeoutsOptions.Default)}' " +
                $"must be between {_minTimeout} and {_maxTimeout}.");
        }

        if (options.Extended < _minTimeout || options.Extended > _maxTimeout)
        {
            failures.Add(
                $"'{RequestTimeoutsOptions.SectionName}:{nameof(RequestTimeoutsOptions.Extended)}' " +
                $"must be between {_minTimeout} and {_maxTimeout}.");
        }

        if (options.Extended <= options.Default)
        {
            failures.Add(
                $"'{RequestTimeoutsOptions.SectionName}:{nameof(RequestTimeoutsOptions.Extended)}' must be " +
                $"greater than '{RequestTimeoutsOptions.SectionName}:{nameof(RequestTimeoutsOptions.Default)}', " +
                "or a long-running endpoint would get no more room than every other one.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

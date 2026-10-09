using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Hosting;

/// <summary>
/// How long a request may run before it is cut loose.
/// <para>
/// Public because it is bound from configuration and its section name is part of the template's
/// contract with whoever deploys it.
/// </para>
/// </summary>
public sealed class RequestTimeoutsOptions
{
    /// <summary>The configuration section this type is bound from.</summary>
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
}

internal sealed class RequestTimeoutsOptionsValidator : IValidateOptions<RequestTimeoutsOptions>
{
    private static readonly TimeSpan _minTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _maxTimeout = TimeSpan.FromHours(1);

    public ValidateOptionsResult Validate(string? name, RequestTimeoutsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Default < _minTimeout || options.Default > _maxTimeout)
        {
            return ValidateOptionsResult.Fail(
                $"'{RequestTimeoutsOptions.SectionName}:{nameof(RequestTimeoutsOptions.Default)}' " +
                $"must be between {_minTimeout} and {_maxTimeout}.");
        }

        return ValidateOptionsResult.Success;
    }
}

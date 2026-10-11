using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Hosting;

/// <summary>How long a request may run before it is cut loose. Public because it is bound from
/// configuration and its section name is part of the template's contract with whoever deploys
/// it.</summary>
public sealed class RequestTimeoutsOptions
{
    /// <summary>The configuration section this type is bound from.</summary>
    public const string SectionName = "RequestTimeouts";

    /// <summary>5 minutes by default, applied to every endpoint that does not name a different
    /// policy.</summary>
    /// <remarks>This must reconcile with the layer underneath: persistence commits with a
    /// 30-second command timeout and up to five retries (<c>EnableRetryOnFailure</c>), so a single
    /// database call can legitimately occupy on the order of 230 seconds. <b>This default must stay
    /// longer than that ceiling:</b> a shorter request timeout would routinely cancel a write still
    /// safely retrying underneath, handing the caller a write whose outcome it can no longer
    /// observe. If the retry budget is ever tightened, shrink this to match; it must never be the
    /// one that moves first.</remarks>
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

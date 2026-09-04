using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Lifecycle;

/// <summary>
/// How long the host waits for in-flight work once shutdown begins.
/// <para>
/// Public because it is bound from configuration and its section name is part of the template's
/// contract with whoever deploys it.
/// </para>
/// </summary>
public sealed class ShutdownOptions
{
    public const string SectionName = "Shutdown";

    /// <summary>
    /// 30 seconds by default: the same grace period Kubernetes gives a pod
    /// (<c>terminationGracePeriodSeconds</c>) before SIGKILL, so this host is not still draining
    /// when the orchestrator stops waiting. Long enough for a normal request — including one
    /// retrying underneath, see <see cref="RequestTimeoutsOptions.Default"/> — to finish; not so
    /// long that a genuinely stuck one holds the process open past what deploys can tolerate.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}

internal sealed class ShutdownOptionsValidator : IValidateOptions<ShutdownOptions>
{
    private static readonly TimeSpan _maxTimeout = TimeSpan.FromMinutes(10);

    public ValidateOptionsResult Validate(string? name, ShutdownOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Timeout <= TimeSpan.Zero || options.Timeout > _maxTimeout)
        {
            return ValidateOptionsResult.Fail(
                $"'{ShutdownOptions.SectionName}:{nameof(ShutdownOptions.Timeout)}' must be greater " +
                $"than zero and at most {_maxTimeout}.");
        }

        return ValidateOptionsResult.Success;
    }
}

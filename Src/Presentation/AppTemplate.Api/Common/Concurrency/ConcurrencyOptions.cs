using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Concurrency;

/// <summary>Whether a caller may change a versioned resource without naming a version.</summary>
public enum IfMatchRequirement
{
    /// <summary>
    /// An unconditional write is accepted. The in-request read-write window is still guarded by the
    /// aggregate's version, so nothing is lost that was protected before; what a caller gives up is
    /// detection of an overwrite decided against a representation it read earlier.
    /// </summary>
    Optional,

    /// <summary>
    /// An unconditional write is refused with 428, so no lost update can go undetected. Every client
    /// must read before it writes and send back what it read.
    /// </summary>
    Required,
}

/// <summary>
/// How much of the concurrency guarantee is delegated to clients.
/// <para>
/// Public because it is bound from configuration and its section name is part of the template's
/// contract with whoever deploys it.
/// </para>
/// </summary>
public sealed class ConcurrencyOptions
{
    public const string SectionName = "Concurrency";

    /// <summary>
    /// <see cref="IfMatchRequirement.Optional"/> by default: turning it on rejects every deployed
    /// client that predates <c>If-Match</c> support, and only the team deploying this knows whether
    /// it has any. See <c>docs/adr/0013-if-match-is-optional-by-default.md</c>.
    /// </summary>
    public IfMatchRequirement IfMatch { get; set; } = IfMatchRequirement.Optional;
}

internal sealed class ConcurrencyOptionsValidator : IValidateOptions<ConcurrencyOptions>
{
    public ValidateOptionsResult Validate(string? name, ConcurrencyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // The binder accepts a number for an enum without checking that it names a member, so
        // 'Concurrency:IfMatch=7' would otherwise boot and fall through to the laxer branch of every
        // switch that reads it.
        if (!Enum.IsDefined(options.IfMatch))
        {
            return ValidateOptionsResult.Fail(
                $"'{ConcurrencyOptions.SectionName}:{nameof(ConcurrencyOptions.IfMatch)}' is "
                + $"'{(int)options.IfMatch}', which is not one of "
                + $"'{nameof(IfMatchRequirement.Optional)}' or '{nameof(IfMatchRequirement.Required)}'.");
        }

        return ValidateOptionsResult.Success;
    }
}

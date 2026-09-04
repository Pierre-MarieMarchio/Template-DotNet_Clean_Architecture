using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Idempotency;

/// <summary>
/// How long a claimed <c>Idempotency-Key</c> is remembered, and the limits that keep the store from
/// growing without bound.
/// <para>
/// Public because it is bound from configuration and its section name is part of the template's
/// contract with whoever deploys it.
/// </para>
/// </summary>
public sealed class IdempotencyOptions
{
    public const string SectionName = "Idempotency";

    /// <summary>
    /// On by default: the capability costs nothing to a caller that never sends the header, and an
    /// action still has to opt in with <see cref="IdempotentAttribute"/> before this does anything.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How long a completed key may still be replayed.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// How long an unfinished claim blocks a retry before it is treated as abandoned — the
    /// claimant's process died before it could call <c>CompleteAsync</c> or <c>ReleaseAsync</c> — and
    /// made reclaimable by whoever asks next.
    /// <para>
    /// Unrelated to <see cref="Retention"/>, which governs how long a <em>completed</em> response
    /// stays replayable; this one only ever matters while a claim is still unfinished. It must stay
    /// comfortably above the longest a legitimate request may run, or it would expire out from under
    /// a claimant that is still working and hand the key to a second, concurrent attempt — the exact
    /// double write this whole mechanism exists to prevent.
    /// </para>
    /// <para>
    /// 15 minutes: half again the 10-minute ceiling of
    /// <see cref="AppTemplate.Api.Common.Hosting.RequestTimeoutsOptions.Extended"/> — the longest an
    /// ordinary (non-streaming) request is ever allowed to run before the platform cuts it off itself,
    /// at which point the filter's own <c>catch</c> releases the claim promptly. The margin is headroom
    /// for clock drift between instances and for the little work still left once the action returns
    /// (writing the response, then <c>CompleteAsync</c>'s own round trip), not a second worst case
    /// stacked on the first. A value below that 10-minute ceiling would turn a slow-but-legitimate
    /// write into a lease expiring while it is still in flight.
    /// </para>
    /// </summary>
    public TimeSpan ClaimLease { get; set; } = TimeSpan.FromMinutes(15);

    public int MaxKeyLength { get; set; } = 128;

    /// <summary>
    /// A stored response body larger than this is dropped rather than truncated, so a later replay
    /// answers <c>idempotency.notReplayable</c> instead of a partial body.
    /// </summary>
    public int MaxStoredResponseBytes { get; set; } = 8192;
}

internal sealed class IdempotencyOptionsValidator : IValidateOptions<IdempotencyOptions>
{
    private static readonly TimeSpan _maxRetention = TimeSpan.FromDays(30);

    public ValidateOptionsResult Validate(string? name, IdempotencyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.Retention <= TimeSpan.Zero || options.Retention > _maxRetention)
        {
            failures.Add(
                $"'{IdempotencyOptions.SectionName}:{nameof(IdempotencyOptions.Retention)}' must be " +
                "greater than zero and at most 30 days.");
        }

        if (options.ClaimLease <= TimeSpan.Zero)
        {
            failures.Add(
                $"'{IdempotencyOptions.SectionName}:{nameof(IdempotencyOptions.ClaimLease)}' must be " +
                "greater than zero.");
        }
        else if (options.ClaimLease > options.Retention)
        {
            // A lease that outlives the row it sits on could never be reclaimed before the row purges
            // on its own — Retention is always the outer bound.
            failures.Add(
                $"'{IdempotencyOptions.SectionName}:{nameof(IdempotencyOptions.ClaimLease)}' must not " +
                $"exceed '{IdempotencyOptions.SectionName}:{nameof(IdempotencyOptions.Retention)}'.");
        }

        if (options.MaxKeyLength is < 1 or > 512)
        {
            failures.Add(
                $"'{IdempotencyOptions.SectionName}:{nameof(IdempotencyOptions.MaxKeyLength)}' must be " +
                "between 1 and 512.");
        }

        if (options.MaxStoredResponseBytes < 1)
        {
            failures.Add(
                $"'{IdempotencyOptions.SectionName}:{nameof(IdempotencyOptions.MaxStoredResponseBytes)}' " +
                "must be at least 1.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

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

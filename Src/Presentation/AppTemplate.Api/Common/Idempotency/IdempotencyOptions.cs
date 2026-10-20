using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Common.Idempotency;

public sealed class IdempotencyOptions
{
    public const string SectionName = "Idempotency";

    public bool Enabled { get; set; } = true;

    public TimeSpan Retention { get; set; } = TimeSpan.FromHours(24);

    public TimeSpan ClaimLease { get; set; } = TimeSpan.FromMinutes(15);

    public int MaxKeyLength { get; set; } = 128;

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

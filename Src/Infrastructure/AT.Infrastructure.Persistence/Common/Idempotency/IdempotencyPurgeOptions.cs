using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Persistence.Common.Idempotency;

public sealed class IdempotencyPurgeOptions
{
    public const string SectionName = "IdempotencyPurge";

    public int BatchSize { get; set; } = 1000;
}

internal sealed class IdempotencyPurgeOptionsValidator : IValidateOptions<IdempotencyPurgeOptions>
{
    public ValidateOptionsResult Validate(string? name, IdempotencyPurgeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.BatchSize is < 1 or > 100_000
            ? ValidateOptionsResult.Fail($"'{IdempotencyPurgeOptions.SectionName}:BatchSize' must be between 1 and 100000.")
            : ValidateOptionsResult.Success;
    }
}

using Microsoft.Extensions.Options;

namespace AppTemplate.Infrastructure.Persistence.Common.Idempotency;

/// <summary>
/// How many expired rows <see cref="IdempotencyStore.PurgeExpiredAsync"/> deletes per round trip.
/// Under sustained ingestion the expired range can be hundreds of thousands of rows; one
/// unbounded <c>DELETE</c> over all of them holds its lock for the whole scan and leaves that
/// much dead-tuple bloat in a single vacuum-eligible burst. A bounded batch, repeated, keeps each
/// individual lock short and lets autovacuum keep pace.
/// </summary>
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

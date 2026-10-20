using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Core.Common.Idempotency;

public sealed record IdempotencyKey
{
    private IdempotencyKey(Guid userId, string key, string endpoint, string fingerprint)
    {
        UserId = userId;
        Key = key;
        Endpoint = endpoint;
        Fingerprint = fingerprint;
    }

    public Guid UserId { get; }

    public string Key { get; }

    public string Endpoint { get; }

    public string Fingerprint { get; }

    public static Result<IdempotencyKey> Create(
        Guid userId,
        string? key,
        string endpoint,
        string fingerprint,
        int maxKeyLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);

        if (string.IsNullOrWhiteSpace(key))
        {
            return Result.Failure<IdempotencyKey>(
                IdempotencyErrors.KeyInvalid("The 'Idempotency-Key' header must not be blank."));
        }

        if (key.Length > maxKeyLength)
        {
            return Result.Failure<IdempotencyKey>(IdempotencyErrors.KeyInvalid(
                $"The 'Idempotency-Key' header must not exceed {maxKeyLength} characters."));
        }

        return Result.Success(new IdempotencyKey(userId, key, endpoint, fingerprint));
    }
}

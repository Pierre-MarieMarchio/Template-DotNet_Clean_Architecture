namespace AppTemplate.Application.Common.Idempotency;

/// <summary>
/// A validated <c>Idempotency-Key</c> header, scoped to the caller and the request it was sent
/// with.
/// </summary>
/// <remarks>
/// <see cref="UserId"/> is what keeps two users from colliding over the same key string, and
/// <see cref="Fingerprint"/> is what tells a genuine retry apart from a different request that
/// happens to reuse the same key.
/// </remarks>
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

    /// <summary>The caller's own <c>Idempotency-Key</c> header value.</summary>
    public string Key { get; }

    /// <summary>The method and path the key was presented against, e.g. <c>POST /api/v1/todo-lists</c>.</summary>
    public string Endpoint { get; }

    /// <summary>Hex SHA-256 of the method, the path and the raw request body.</summary>
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

namespace AppTemplate.Application.Core.Common.Results;

/// <param name="Code">Stable, dotted identifier clients may branch on, e.g. <c>todoList.notFound</c>.</param>
/// <param name="Message">Human-readable description, safe to return to a client.</param>
/// <param name="Details">Per-field messages, keyed by field path. Populated for validation failures.</param>
public sealed record Error(
    string Code,
    string Message,
    ErrorType Type,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Details = null)
{
    /// <summary>A request this application will not act on, with nothing to pin on one field.</summary>
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    /// <summary>
    /// The same, carrying the per-field messages a form needs in order to mark up its own inputs rather
    /// than show one sentence.
    /// </summary>
    public static Error Validation(
        string code,
        string message,
        IReadOnlyDictionary<string, IReadOnlyList<string>> details) =>
        new(code, message, ErrorType.Validation, details);

    /// <summary>Nothing answers to the identifier the caller named.</summary>
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    /// <summary>The caller has not said who they are; the answer is to authenticate and come back.</summary>
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);

    /// <summary>The caller is known and still not allowed; authenticating again changes nothing.</summary>
    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);

    /// <summary>The request is well formed but the current state rules it out.</summary>
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    /// <summary>
    /// The condition the caller attached does not hold, so a retry has to begin with a fresh read.
    /// </summary>
    public static Error PreconditionFailed(string code, string message) =>
        new(code, message, ErrorType.PreconditionFailed);

    /// <summary>
    /// The operation is conditional-only and the caller attached no condition: it is refused rather than
    /// applied blind.
    /// </summary>
    public static Error PreconditionRequired(string code, string message) =>
        new(code, message, ErrorType.PreconditionRequired);

    // The generated equality would compare Details by reference: two errors built from separate
    // dictionaries with the same keys and values would count as different. Comparing structurally
    // means overriding GetHashCode too, since the two must agree.
    /// <summary>
    /// Equal on the same code, message, type and the same <em>contents</em> of <see cref="Details"/> —
    /// not the same dictionary instance.
    /// </summary>
    public bool Equals(Error? other) =>
        other is not null
        && Code == other.Code
        && Message == other.Message
        && Type == other.Type
        && DetailsEqual(Details, other.Details);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(Code);
        hash.Add(Message);
        hash.Add(Type);

        if (Details is not null)
        {
            foreach (var pair in Details.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                hash.Add(pair.Key);

                foreach (string value in pair.Value)
                {
                    hash.Add(value);
                }
            }
        }

        return hash.ToHashCode();
    }

    private static bool DetailsEqual(
        IReadOnlyDictionary<string, IReadOnlyList<string>>? left,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var otherValue) || !pair.Value.SequenceEqual(otherValue))
            {
                return false;
            }
        }

        return true;
    }
}

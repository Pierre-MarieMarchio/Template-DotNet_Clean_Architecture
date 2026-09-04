namespace AppTemplate.Application.Common.Results;

/// <param name="Code">Stable, dotted identifier clients may branch on, e.g. <c>todoList.notFound</c>.</param>
/// <param name="Message">Human-readable description, safe to return to a client.</param>
/// <param name="Details">Per-field messages, keyed by field path. Populated for validation failures.</param>
public sealed record Error(
    string Code,
    string Message,
    ErrorType Type,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? Details = null)
{
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    public static Error Validation(
        string code,
        string message,
        IReadOnlyDictionary<string, IReadOnlyList<string>> details) =>
        new(code, message, ErrorType.Validation, details);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error PreconditionFailed(string code, string message) =>
        new(code, message, ErrorType.PreconditionFailed);

    public static Error PreconditionRequired(string code, string message) =>
        new(code, message, ErrorType.PreconditionRequired);

    // The generated equality would compare Details by reference: two errors built from separate
    // dictionaries with the same keys and values would count as different. Comparing structurally
    // means overriding GetHashCode too, since the two must agree.
    public bool Equals(Error? other) =>
        other is not null
        && Code == other.Code
        && Message == other.Message
        && Type == other.Type
        && DetailsEqual(Details, other.Details);

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

using AppTemplate.Application.Common.Results;

namespace AppTemplate.Application.Common.Collections;

/// <summary>
/// A caller's free-text search input, trimmed and length-checked. It knows nothing about SQL or
/// <c>LIKE</c>: turning it into a safe pattern is a persistence-layer concern, because only that
/// layer knows which wildcard characters its own query engine gives special meaning to.
/// </summary>
public sealed record SearchTerm
{
    public const int MaxLength = 100;

    private SearchTerm(string value) => Value = value;

    public string Value { get; }

    internal static Result<SearchTerm> Create(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        string trimmed = value.Trim();

        if (trimmed.Length > MaxLength)
        {
            return Result.Failure<SearchTerm>(
                CollectionErrors.InvalidFilter($"A search term cannot exceed {MaxLength} characters."));
        }

        return Result.Success(new SearchTerm(trimmed));
    }
}

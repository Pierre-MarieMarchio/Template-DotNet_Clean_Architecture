using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Core.Common.Collections;

/// <summary>
/// A caller's free-text search input, trimmed and length-checked. It knows nothing about SQL or
/// <c>LIKE</c>: turning it into a safe pattern is a persistence-layer concern, because only that
/// layer knows which wildcard characters its own query engine gives special meaning to.
/// </summary>
public sealed record SearchTerm
{
    /// <summary>
    /// The ceiling <see cref="Create"/> refuses past, so an oversized term never reaches the read side.
    /// </summary>
    public const int MaxLength = 100;

    private SearchTerm(string value) => Value = value;

    /// <summary>
    /// Trimmed, and otherwise exactly what the caller typed: escaping is left to the layer that knows its
    /// own query engine's wildcards.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Trims before measuring, so surrounding whitespace never costs a caller their term, and returns a
    /// failure rather than throwing when what is left is still too long.
    /// </summary>
    public static Result<SearchTerm> Create(string value)
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

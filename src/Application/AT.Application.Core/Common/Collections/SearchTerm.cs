using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Core.Common.Collections;

public sealed record SearchTerm
{
    public const int MaxLength = 100;

    private SearchTerm(string value) => Value = value;

    public string Value { get; }

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

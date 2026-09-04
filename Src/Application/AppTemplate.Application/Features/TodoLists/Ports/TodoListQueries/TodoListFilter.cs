using System.Globalization;
using AppTemplate.Application.Common.Collections;
using AppTemplate.Application.Common.Results;

namespace AppTemplate.Application.Features.TodoLists.Ports.TodoListQueries;

/// <summary>
/// The to-do list feature's own filter. <see cref="Search"/> matches against the list's <c>name</c>
/// only, case-insensitively, as a contains — and not accent-insensitively: that would need a
/// collation decision this schema does not make.
/// </summary>
public sealed record TodoListFilter
{
    public static readonly TodoListFilter None = new(null, null, null);

    private TodoListFilter(SearchTerm? search, DateTimeOffset? createdAfter, DateTimeOffset? createdBefore)
    {
        Search = search;
        CreatedAfter = createdAfter;
        CreatedBefore = createdBefore;
    }

    public SearchTerm? Search { get; }

    public DateTimeOffset? CreatedAfter { get; }

    public DateTimeOffset? CreatedBefore { get; }

    public static Result<TodoListFilter> Create(string? search, string? createdAfter, string? createdBefore)
    {
        SearchTerm? searchTerm = null;

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchResult = SearchTerm.Create(search);

            if (searchResult.IsFailure)
            {
                return searchResult.To<TodoListFilter>();
            }

            searchTerm = searchResult.Value;
        }

        DateTimeOffset? after = null;

        if (!string.IsNullOrWhiteSpace(createdAfter))
        {
            if (!DateTimeOffset.TryParse(
                createdAfter,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset parsed))
            {
                return Result.Failure<TodoListFilter>(
                    CollectionErrors.InvalidFilter("'createdAfter' is not a valid ISO 8601 date/time."));
            }

            after = parsed;
        }

        DateTimeOffset? before = null;

        if (!string.IsNullOrWhiteSpace(createdBefore))
        {
            if (!DateTimeOffset.TryParse(
                createdBefore,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset parsed))
            {
                return Result.Failure<TodoListFilter>(
                    CollectionErrors.InvalidFilter("'createdBefore' is not a valid ISO 8601 date/time."));
            }

            before = parsed;
        }

        // An empty window is a caller mistake, not an empty page: answering zero rows for it would
        // look identical to a window that legitimately matched nothing.
        if (after is { } lower && before is { } upper && lower > upper)
        {
            return Result.Failure<TodoListFilter>(
                CollectionErrors.InvalidFilter("'createdAfter' must not be later than 'createdBefore'."));
        }

        return Result.Success(new TodoListFilter(searchTerm, after, before));
    }
}

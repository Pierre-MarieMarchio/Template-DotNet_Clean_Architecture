using AppTemplate.Application.Core.Common.Collections;
using AppTemplate.Application.Core.Common.Results;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;

namespace AppTemplate.Application.Features.Files.Ports.StoredFileQueries;

/// <summary>
/// A closed set of typed parameters, never a composable expression (see <c>docs/DECISIONS.md</c>).
/// <see cref="Search"/> is a case-insensitive contains over the name only; the object key addresses bytes and is not
/// the user's to search.
/// </summary>
public sealed record StoredFileFilter
{
    public static readonly StoredFileFilter None = new(null, null);

    private StoredFileFilter(SearchTerm? search, StoredFileState? state)
    {
        Search = search;
        State = state;
    }

    public SearchTerm? Search { get; }

    /// <summary>
    /// Which part of the life to show: "uploads that never finished" (pending) when the quota is full, "why can I not
    /// download this?" (quarantined).
    /// </summary>
    public StoredFileState? State { get; }

    public static Result<StoredFileFilter> Create(string? search, string? state)
    {
        SearchTerm? searchTerm = null;

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchResult = SearchTerm.Create(search);

            if (searchResult.IsFailure)
            {
                return searchResult.To<StoredFileFilter>();
            }

            searchTerm = searchResult.Value;
        }

        StoredFileState? parsedState = null;

        if (!string.IsNullOrWhiteSpace(state))
        {
            // By name, not Enum.TryParse, which also accepts numbers: "7" would parse into a state
            // nothing handles and surface as an empty page.
            parsedState = state.Trim().ToLowerInvariant() switch
            {
                "pending" => StoredFileState.Pending,
                "deposited" => StoredFileState.Deposited,
                "available" => StoredFileState.Available,
                "quarantined" => StoredFileState.Quarantined,
                _ => null,
            };

            if (parsedState is null)
            {
                return Result.Failure<StoredFileFilter>(
                    CollectionErrors.InvalidFilter(
                        "'state' must be 'pending', 'deposited', 'available' or 'quarantined'."));
            }
        }

        return Result.Success(new StoredFileFilter(searchTerm, parsedState));
    }
}

using AppTemplate.Application.Common.Collections;
using AppTemplate.Application.Common.Results;
using AppTemplate.Domain.Features.Files.ValueObjects;

namespace AppTemplate.Application.Features.Files.Ports.StoredFileQueries;

/// <summary>
/// The file feature's own filter: a closed set of typed parameters, never an expression a caller
/// composes — see <c>CONTRIBUTING.md</c> for why there is no filter language. Adding a filter here
/// means adding a parameter and a test, which is the point.
/// <para>
/// <see cref="Search"/> matches the file's name only, case-insensitively, as a contains. The name is
/// a label the user chose; the object key is not searchable and is not meant to be, since it
/// addresses bytes and nothing about it is the user's to look for.
/// </para>
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
    /// Which part of the life to show. Worth having as a filter rather than inferring: "the uploads
    /// that never finished" is the question a user asks when their quota is full, and the answer is
    /// exactly <see cref="StoredFileState.Pending"/> — while "why can I not download this?" is
    /// answered by <see cref="StoredFileState.Quarantined"/>, which is the whole reason a refused
    /// file keeps a row.
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
            // Matched by name against the members this enum has, rather than through Enum.TryParse:
            // that helper also accepts the underlying number, so "7" would parse into a
            // StoredFileState no switch anywhere handles, and the failure would surface as an empty
            // page rather than as a refused request.
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

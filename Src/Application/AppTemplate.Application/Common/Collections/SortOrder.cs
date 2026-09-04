using AppTemplate.Application.Common.Results;

namespace AppTemplate.Application.Common.Collections;

/// <summary>A caller's <c>sort</c> string, parsed and checked against a feature's whitelist.</summary>
public sealed record SortOrder
{
    private SortOrder(IReadOnlyList<SortTerm> terms) => Terms = terms;

    /// <summary>Never empty: a blank input parses <see cref="ICollectionPolicy.DefaultSort"/> instead.</summary>
    public IReadOnlyList<SortTerm> Terms { get; }

    /// <summary>
    /// Comma-separated terms, each <c>field</c> or <c>field:asc</c>/<c>field:desc</c> (the direction
    /// token is case-insensitive; a bare field means ascending).
    /// </summary>
    /// <remarks>
    /// A blank <paramref name="raw"/> parses <paramref name="policy"/>'s own
    /// <see cref="ICollectionPolicy.DefaultSort"/> through this same method body, so a feature's
    /// default is whitelisted by exactly the code path a caller's input takes. That default must
    /// therefore not itself be blank — recursing on a blank default would never terminate, so that
    /// case is a programming error in the policy, not a request to reject.
    /// </remarks>
    public static Result<SortOrder> Parse(string? raw, ICollectionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (string.IsNullOrWhiteSpace(raw))
        {
            if (string.IsNullOrWhiteSpace(policy.DefaultSort))
            {
                throw new InvalidOperationException(
                    $"{policy.GetType().Name}.DefaultSort must not be blank: a blank default is parsed "
                    + "by this same method, which would recurse forever.");
            }

            return Parse(policy.DefaultSort, policy);
        }

        string[] rawTerms = raw.Split(',');

        if (rawTerms.Length > policy.MaxSortTerms)
        {
            return Result.Failure<SortOrder>(CollectionErrors.InvalidSort(
                $"'sort' may carry at most {policy.MaxSortTerms} term(s); '{raw}' carries {rawTerms.Length}."));
        }

        var terms = new List<SortTerm>(rawTerms.Length);
        var seenFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string rawTerm in rawTerms)
        {
            var termResult = ParseTerm(rawTerm, policy, seenFields);

            if (termResult.IsFailure)
            {
                return termResult.To<SortOrder>();
            }

            terms.Add(termResult.Value);
        }

        return Result.Success(new SortOrder(terms));
    }

    private static Result<SortTerm> ParseTerm(
        string rawTerm,
        ICollectionPolicy policy,
        HashSet<string> seenFields)
    {
        if (string.IsNullOrWhiteSpace(rawTerm))
        {
            return Result.Failure<SortTerm>(CollectionErrors.InvalidSort(
                $"'sort' cannot contain an empty term. Legal fields are: {LegalFields(policy)}."));
        }

        string[] parts = rawTerm.Split(':');

        if (parts.Length > 2)
        {
            return Result.Failure<SortTerm>(CollectionErrors.InvalidSort(
                $"'{rawTerm}' is not a valid sort term: expected 'field' or 'field:asc'/'field:desc'."));
        }

        string fieldToken = parts[0].Trim();
        SortDirection direction = SortDirection.Ascending;

        if (parts.Length == 2)
        {
            string directionToken = parts[1].Trim();

            if (string.Equals(directionToken, "asc", StringComparison.OrdinalIgnoreCase))
            {
                direction = SortDirection.Ascending;
            }
            else if (string.Equals(directionToken, "desc", StringComparison.OrdinalIgnoreCase))
            {
                direction = SortDirection.Descending;
            }
            else
            {
                return Result.Failure<SortTerm>(CollectionErrors.InvalidSort(
                    $"'{directionToken}' is not a valid sort direction: expected 'asc' or 'desc'."));
            }
        }

        var field = policy.SortableFields.FirstOrDefault(
            candidate => string.Equals(candidate.Name, fieldToken, StringComparison.OrdinalIgnoreCase));

        if (field is null)
        {
            return Result.Failure<SortTerm>(CollectionErrors.InvalidSort(
                $"'{fieldToken}' is not a sortable field. Legal fields are: {LegalFields(policy)}."));
        }

        if (!seenFields.Add(field.Name))
        {
            return Result.Failure<SortTerm>(CollectionErrors.InvalidSort(
                $"'{field.Name}' is named more than once in 'sort'."));
        }

        return Result.Success(SortTerm.Of(field.Name, direction));
    }

    private static string LegalFields(ICollectionPolicy policy) =>
        string.Join(", ", policy.SortableFields.Select(field => field.Name));
}

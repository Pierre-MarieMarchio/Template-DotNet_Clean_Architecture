using AppTemplate.Application.Core.Common.Policies;
using AppTemplate.Application.Core.Common.Results;

namespace AppTemplate.Application.Core.Common.Collections;

public sealed record SortOrder
{
    private SortOrder(IReadOnlyList<SortTerm> terms) => Terms = terms;

    public IReadOnlyList<SortTerm> Terms { get; }

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

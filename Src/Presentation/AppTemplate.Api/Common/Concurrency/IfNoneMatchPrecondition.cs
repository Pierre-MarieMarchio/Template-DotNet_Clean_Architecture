using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace AppTemplate.Api.Common.Concurrency;

internal static class IfNoneMatchPrecondition
{
    private const string _any = "*";

    internal static bool Matches(HttpRequest request, string currentETag)
    {
        ArgumentNullException.ThrowIfNull(request);

        var values = request.Headers.IfNoneMatch;

        if (StringValues.IsNullOrEmpty(values))
        {
            return false;
        }

        if (values.Count == 1 && string.Equals(values[0]?.Trim(), _any, StringComparison.Ordinal))
        {
            return true;
        }

        if (!EntityTagHeaderValue.TryParseStrictList(values, out var tags))
        {
            return false;
        }

        var current = new EntityTagHeaderValue(currentETag);

        return tags.Any(tag => tag.Compare(current, useStrongComparison: false));
    }
}

using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace AppTemplate.Api.Common.Concurrency;

/// <summary>
/// The read side of the same validator: whether the caller already holds the representation about
/// to be sent.
/// </summary>
internal static class IfNoneMatchPrecondition
{
    private const string _any = "*";

    /// <summary>
    /// True when the caller's <c>If-None-Match</c> names <paramref name="currentETag"/>, and the
    /// answer is therefore 304 rather than the body.
    /// </summary>
    /// <remarks>
    /// Compared with the weak function, which is what RFC 9110 specifies for this header. Every tag
    /// this API issues is strong, so the two functions agree on them; using the specified one means
    /// a caller that echoes back <c>W/"…"</c> from an intermediary is still served correctly.
    /// <para>
    /// A malformed value answers false — the request then gets its representation. That is the safe
    /// direction: the alternative is a 304 for a version the caller does not have.
    /// </para>
    /// </remarks>
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

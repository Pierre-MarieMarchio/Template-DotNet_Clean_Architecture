using AppTemplate.Application.Common.Concurrency;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace AppTemplate.Api.Common.Concurrency;

/// <param name="Required">
/// The versions the tags name, or <c>null</c> when the header states no version at all.
/// </param>
internal sealed record IfMatchPrecondition(IfMatchState State, VersionPrecondition? Required)
{
    private const string _any = "*";

    /// <summary>
    /// Reads the header without deciding anything about it: a malformed value is reported rather
    /// than ignored, because ignoring one turns a conditional write into an unconditional one.
    /// </summary>
    internal static IfMatchPrecondition Read(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var values = request.Headers.IfMatch;

        if (StringValues.IsNullOrEmpty(values))
        {
            return new(IfMatchState.Absent, null);
        }

        // Recognised before parsing, so this does not depend on how the entity-tag grammar's parser
        // chooses to treat a wildcard.
        if (values.Count == 1 && string.Equals(values[0]?.Trim(), _any, StringComparison.Ordinal))
        {
            return new(IfMatchState.Any, null);
        }

        // Strict, so a list with one bad entry is a bad list. The lenient overload drops what it
        // cannot parse, which would silently narrow the caller's condition.
        if (!EntityTagHeaderValue.TryParseStrictList(values, out var tags) || tags.Count == 0)
        {
            return new(IfMatchState.Malformed, null);
        }

        var versions = new List<uint>(tags.Count);

        foreach (var tag in tags)
        {
            if (EntityTagMapping.TryReadVersion(tag, out uint version))
            {
                versions.Add(version);
            }
        }

        // An empty list here is a condition nothing satisfies, not an absent one: every tag the
        // caller sent was weak, or was never issued by this API.
        return new(IfMatchState.Tags, new VersionPrecondition(versions));
    }
}

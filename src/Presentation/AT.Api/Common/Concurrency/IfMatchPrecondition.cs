using AppTemplate.Application.Core.Common.Concurrency;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace AppTemplate.Api.Common.Concurrency;

internal sealed record IfMatchPrecondition(IfMatchState State, VersionPrecondition? Required)
{
    private const string _any = "*";

    internal static IfMatchPrecondition Read(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var values = request.Headers.IfMatch;

        if (StringValues.IsNullOrEmpty(values))
        {
            return new(IfMatchState.Absent, null);
        }

        if (values.Count == 1 && string.Equals(values[0]?.Trim(), _any, StringComparison.Ordinal))
        {
            return new(IfMatchState.Any, null);
        }

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

        return new(IfMatchState.Tags, new VersionPrecondition(versions));
    }
}

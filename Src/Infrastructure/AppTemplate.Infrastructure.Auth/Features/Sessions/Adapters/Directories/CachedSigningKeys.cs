using System.Collections.Concurrent;
using Microsoft.IdentityModel.Tokens;

namespace AppTemplate.Infrastructure.Auth.Features.Sessions.Adapters.Directories;

internal sealed class CachedSigningKeys
{
    private readonly ConcurrentDictionary<string, SigningKeySet> _sets = new(StringComparer.OrdinalIgnoreCase);

    internal SigningKeySet? Find(string provider) =>
        _sets.TryGetValue(provider, out var set) ? set : null;

    internal void Store(string provider, SigningKeySet set) => _sets[provider] = set;
}

internal sealed record SigningKeySet(
    IReadOnlyCollection<SecurityKey> Keys,
    DateTimeOffset FetchedAt,
    DateTimeOffset AttemptedAt);

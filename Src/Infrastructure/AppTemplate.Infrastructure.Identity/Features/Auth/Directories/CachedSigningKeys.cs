using System.Collections.Concurrent;
using Microsoft.IdentityModel.Tokens;

namespace AppTemplate.Infrastructure.Identity.Features.Auth.Directories;

/// <summary>
/// The key sets fetched so far, one per provider.
/// <para>
/// A singleton holding nothing but state, for the same reason <c>RecordedEmails</c> and
/// <c>StoredObjects</c> are: the thing that fetches is a typed <c>HttpClient</c> and therefore
/// transient — the factory hands it a fresh handler so sockets and DNS rotate — and a cache that
/// lived on a transient would be a cache that never hit.
/// </para>
/// </summary>
internal sealed class CachedSigningKeys
{
    private readonly ConcurrentDictionary<string, SigningKeySet> _sets = new(StringComparer.OrdinalIgnoreCase);

    internal SigningKeySet? Find(string provider) =>
        _sets.TryGetValue(provider, out var set) ? set : null;

    internal void Store(string provider, SigningKeySet set) => _sets[provider] = set;
}

/// <summary>
/// What is known about one provider's keys, and the two timestamps that answer two different
/// questions.
/// </summary>
/// <param name="FetchedAt">
/// When the provider last <em>answered</em>. This is what the cache lifetime is measured from, so a
/// provider that goes down does not silently extend the life of the keys it served before it did.
/// </param>
/// <param name="AttemptedAt">
/// When it was last <em>asked</em>, successfully or not. This is what the forced-refresh floor is
/// measured from: a flood of tokens naming a key nobody ever published must cost one request, not
/// one request each, and counting only successes would make a provider that is down the cheapest
/// way to hammer it.
/// </param>
internal sealed record SigningKeySet(
    IReadOnlyCollection<SecurityKey> Keys,
    DateTimeOffset FetchedAt,
    DateTimeOffset AttemptedAt);

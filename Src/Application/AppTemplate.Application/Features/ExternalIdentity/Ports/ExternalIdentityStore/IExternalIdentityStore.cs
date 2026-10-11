using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Features.ExternalIdentity.Ports.ExternalIdentityStore;

/// <summary>
/// The mapping from a provider's subject to an internal owner. Each write commits on its own, outside
/// any request's unit of work: the mapping is settled during authentication, before the operation the
/// request is for has staged anything, and that operation failing must not undo it.
/// </summary>
public interface IExternalIdentityStore
{
    /// <returns>The mapping for the pair, or <c>null</c> when none exists yet.</returns>
    Task<ExternalIdentityLink?> FindAsync(
        string issuer,
        string subject,
        CancellationToken cancellationToken = default);

    /// <summary>Inserts the mapping and commits it.</summary>
    /// <returns>
    /// <c>true</c> when this call created it; <c>false</c> when a mapping for the same issuer and
    /// subject already existed, which a concurrent first request may have just inserted. Never a
    /// second row for the pair: a unique index on it is what decides.
    /// </returns>
    Task<bool> TryAddAsync(ExternalIdentityLink link, CancellationToken cancellationToken = default);

    /// <summary>
    /// Overwrites the email, display name and last-seen instant of the existing mapping for the
    /// pair, and commits. The owner id is never rewritten.
    /// </summary>
    Task RefreshAsync(ExternalIdentityLink link, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every mapping <paramref name="userId"/> is known by, and the role assignments held
    /// for it, and commits — the administrative deletion of an account, whose local half this is.
    /// Both go in one transaction: an owner whose mappings are gone but whose roles remain is
    /// neither deleted nor half of anything.
    /// </summary>
    /// <returns>How many mappings were removed. Role assignments removed is nobody's answer: no
    /// mapping naming the owner can be left to resolve against them.</returns>
    Task<int> RemoveAsync(UserId userId, CancellationToken cancellationToken = default);
}

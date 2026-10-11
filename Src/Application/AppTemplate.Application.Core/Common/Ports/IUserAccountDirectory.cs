using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Core.Common.Ports;

/// <summary>
/// Whether an id still names an account. The contract the account-deleted reaction rests on: a
/// consumer of <see cref="AppTemplate.Application.Core.Common.Events.UserAccountDeleted"/> and the
/// sweep behind it both re-derive from the directory rather than trust that the fact reached them,
/// so a delivery that is late, duplicated or replayed cannot delete the data of an account that
/// exists.
/// <para>
/// Accounts owns no feature here, and this port names none. The host supplies the adapter: over
/// the local accounts when the template keeps its own, over the external-identity mapping when it
/// does not — the same choice <c>ICurrentUser</c> is resolved by.
/// </para>
/// </summary>
public interface IUserAccountDirectory
{
    /// <returns>Whether an account exists under <paramref name="userId"/> right now.</returns>
    Task<bool> ExistsAsync(UserId userId, CancellationToken cancellationToken = default);
}

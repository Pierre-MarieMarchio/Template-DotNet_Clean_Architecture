using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Core.Common.Ports;

/// <summary>Whether an id still names an account — the contract the account-deleted reaction rests
/// on: the consumer of <see cref="AppTemplate.Application.Core.Common.Events.UserAccountDeleted"/>
/// and the sweep behind it both re-derive from the directory rather than trust that the fact reached
/// them, so a late, duplicated or replayed delivery cannot delete the data of an account that
/// exists. Accounts owns no feature here; the host supplies the adapter, over the local accounts or
/// the external-identity mapping, the same choice <c>ICurrentUser</c> is resolved by.</summary>
public interface IUserAccountDirectory
{
    /// <returns>Whether an account exists under <paramref name="userId"/> right now.</returns>
    Task<bool> ExistsAsync(UserId userId, CancellationToken cancellationToken = default);
}

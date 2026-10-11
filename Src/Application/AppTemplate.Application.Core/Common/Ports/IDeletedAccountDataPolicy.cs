using AppTemplate.Domain.Core.Common.Primitives;

namespace AppTemplate.Application.Core.Common.Ports;

/// <summary>
/// What one feature does with the data of an account that is gone, declared by that feature and
/// applied by it: the fate of a deleted account's rows is their owner's decision, not the
/// account's, and not the sweep's. A feature that holds user data implements this alongside its
/// consumer of <see cref="AppTemplate.Application.Core.Common.Events.UserAccountDeleted"/> —
/// usually as the same class — and registers both with its own composition. The consumer is the
/// fast path, the sweep is the guarantee, and this port is the one path they share, which is why
/// <see cref="ApplyAsync"/> re-derives the whole precondition rather than assuming the account is
/// gone because somebody said so.
/// </summary>
public interface IDeletedAccountDataPolicy
{
    /// <summary>Applies this feature's policy — delete or anonymise, whichever it declared — to the data one owner still holds here.</summary>
    /// <remarks>
    /// One call is one transaction of this feature's own, committed before it returns — the
    /// account's deletion has already committed, and a failure here must never be reported as a
    /// failure of that. The precondition is re-derived through <see cref="IUserAccountDirectory"/>:
    /// when the account exists again, or never left, nothing is written. A failure propagates to
    /// the caller — the event's consumer logs it, the sweep retries the owner at its next pass —
    /// and the next attempt starts from the precondition, never from a partial run.
    /// </remarks>
    /// <param name="owner">The deleted account to act on.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns><c>true</c> when this call removed or rewrote data; <c>false</c> when there was none, or the account names an account again.</returns>
    Task<bool> ApplyAsync(UserId owner, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every owner id that still has data here, whatever its account's fate: the sweep asks for
    /// the ids and decides the fate itself, through the directory, so the same question is never
    /// answered two ways in one pass.
    /// </summary>
    /// <param name="cancellationToken">Cancels the listing.</param>
    Task<IReadOnlyList<UserId>> ListDataOwnerIdsAsync(CancellationToken cancellationToken = default);
}

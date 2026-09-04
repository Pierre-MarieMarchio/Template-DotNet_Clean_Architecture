namespace AppTemplate.Application.Features.Auth.Ports.AccountDeletion;

/// <summary>
/// Removing an account outright.
/// <para>
/// <b>What this does not do: touch a to-do list or a reminder.</b> Both carry an <c>OwnerId</c> that
/// is a plain <see cref="Guid"/> with no foreign key back to the account table — deliberately, so
/// that a project built from this template can delete either feature and still compile an account.
/// This module has no reference to either and must keep it that way: reaching for a repository from
/// either one here would mean this vertical no longer builds without them, which is exactly backwards
/// for a capability every derived project keeps.
/// </para>
/// <para>
/// <b>What becomes of their rows.</b> Nothing, synchronously. They are left owned by an id that no
/// longer names an account — visible to no one, since every query in those verticals scopes by the
/// caller's own id and a deleted account can no longer authenticate as anyone. A project that keeps
/// those features owes them the treatment this template gives every other kind of stale row: a
/// scheduled purge alongside <c>MaintenanceController</c>'s expired-refresh-token and
/// expired-idempotency-key ones, scoped to rows whose owner no longer resolves.
/// </para>
/// </summary>
public interface IAccountDeletionService
{
    Task<AccountDeletionStatus> DeleteAsync(Guid userId, CancellationToken cancellationToken = default);
}

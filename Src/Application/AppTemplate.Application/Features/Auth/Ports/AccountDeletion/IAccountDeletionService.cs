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
/// caller's own id and a deleted account can no longer authenticate as anyone. That is not an
/// oversight: this template already has a home for exactly this shape of problem — data that outlives
/// the event that made it stale and needs sweeping on a schedule rather than synchronously — in
/// <c>MaintenanceController</c>'s expired-refresh-token and expired-idempotency-key purges. A project
/// that keeps those features owes them the same treatment: a purge reachable the same way, scoped to
/// rows whose owner no longer resolves. Refusing deletion until the account is provably empty, or
/// reassigning ownership to some placeholder, both need the same forbidden reference this port
/// refuses to take on, for no real gain: either still leaves a decision — what counts as empty, who
/// if anyone owns the placeholder — that belongs to the feature that defines <c>OwnerId</c>, not to
/// this one.
/// </para>
/// </summary>
public interface IAccountDeletionService
{
    Task<AccountDeletionStatus> DeleteAsync(Guid userId, CancellationToken cancellationToken = default);
}

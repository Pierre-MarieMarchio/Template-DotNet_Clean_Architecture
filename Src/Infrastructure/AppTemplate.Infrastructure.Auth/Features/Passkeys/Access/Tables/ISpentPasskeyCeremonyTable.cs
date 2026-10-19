namespace AppTemplate.Infrastructure.Auth.Features.Passkeys.Access.Tables;

/// <summary>
/// The record of every passkey ceremony whose state has already been presented, which is what
/// makes a state single-use: WebAuthn requires a challenge to be answered once, and the state
/// carries the challenge. Rows in and rows out, nothing about policy — how a state is sealed, how
/// long it lives and what a refusal answers are the passkey adapter's. A <em>table</em>, for the
/// reason <c>IRefreshTokenTable</c> is one, and internal, unlike that one: nothing outside this
/// module has any business knowing a ceremony was spent. Both methods commit on their own rather
/// than through <c>IUnitOfWork</c>: a spend must be durable before the ceremony is verified, whatever
/// the request does next.
/// </summary>
internal interface ISpentPasskeyCeremonyTable
{
    /// <summary>
    /// Records a state as spent. One INSERT keyed on the hash, committed on its own, so the database
    /// picks the winner between two simultaneous presentations — on one instance or several — and
    /// reports it as the affected-row count; nothing depends on what a preceding read saw.
    /// </summary>
    /// <param name="stateHash">Base64url SHA-256 of the unsealed state, never the state itself.</param>
    /// <param name="expiresAt">When the state stops being redeemable; the row is kept until then.</param>
    /// <returns><c>false</c> when the state was already spent, by an earlier presentation or a
    /// concurrent one.</returns>
    Task<bool> TrySpendAsync(string stateHash, DateTimeOffset expiresAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every record that expired at or before <paramref name="cutoff"/>. Idempotent, so two
    /// instances sweeping at once delete each row once between them, and needs no lease.
    /// </summary>
    /// <returns>How many records were deleted.</returns>
    Task<int> PurgeExpiredAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
}

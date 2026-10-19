using AppTemplate.Infrastructure.Auth.Features.Passkeys.Access.Tables;

namespace AppTemplate.Infrastructure.Auth.Features.Passkeys.Rows.Models;

/// <summary>
/// A passkey ceremony whose state has been presented once and may never be presented again. Kept
/// only until the state itself expires: past that instant the data protector refuses the state on
/// its own, and the record has nothing left to guard.
/// <para>
/// Internal, like <c>RefreshToken</c>: reached only through <see cref="ISpentPasskeyCeremonyTable"/>,
/// which speaks in hashes and instants and never hands this type out.
/// </para>
/// </summary>
internal sealed class SpentPasskeyCeremonyRecord
{
    /// <summary>
    /// Base64url SHA-256 of the unsealed ceremony state, which carries the ceremony's random
    /// challenge, so no two ceremonies share one. The key itself: a second insert of the same hash
    /// is the replay, refused by the database rather than by a preceding read.
    /// </summary>
    public required string StateHash { get; set; }

    /// <summary>When the state stops being redeemable, and so when this record may go.</summary>
    public required DateTimeOffset ExpiresAt { get; set; }
}

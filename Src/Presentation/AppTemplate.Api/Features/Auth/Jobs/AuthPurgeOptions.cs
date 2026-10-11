namespace AppTemplate.Api.Features.Auth.Jobs;

/// <summary>
/// The off switches of authentication's two purges, read from the maintenance section next to
/// the idempotency purge's: <c>Jobs:Maintenance:PurgeExpiredRefreshTokensEnabled</c> and
/// <c>Jobs:Maintenance:PurgeExpiredSecurityEventsEnabled</c>.
/// <para>
/// A class of its own, owned by the feature that owns the purges, so that a project built without
/// authentication loses the keys together with them and the maintenance options never name them.
/// Internal, and without a section name of its own: the section is the maintenance timer's.
/// </para>
/// </summary>
internal sealed class AuthPurgeOptions
{
    public bool PurgeExpiredRefreshTokensEnabled { get; set; } = true;

    public bool PurgeExpiredSecurityEventsEnabled { get; set; } = true;
}

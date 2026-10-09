namespace AppTemplate.Api.Features.Auth.Jobs;

/// <summary>
/// The off switch of the refresh-token purge, read from the maintenance section next to the other
/// purge's: <c>MaintenanceWorker:PurgeExpiredRefreshTokensEnabled</c>.
/// <para>
/// A class of its own, owned by the feature that owns the purge, so that a project built without
/// authentication loses the key together with the purge and the maintenance options never name it.
/// Internal, and without a section name of its own: the section is the maintenance timer's.
/// </para>
/// </summary>
internal sealed class RefreshTokenPurgeOptions
{
    public bool PurgeExpiredRefreshTokensEnabled { get; set; } = true;
}

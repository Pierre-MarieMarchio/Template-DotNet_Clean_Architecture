namespace AppTemplate.Api.Features.BackgroundJobs.Jobs;

/// <summary>
/// The off switch of the deleted-account data sweep, read from the maintenance section next to the
/// other purges': <c>MaintenanceWorker:SweepDeletedAccountsDataEnabled</c>.
/// <para>
/// A class of its own, for the same reason <c>RefreshTokenPurgeOptions</c> is: a project generated
/// without accounts loses the key together with the sweep. Internal, and without a section name of
/// its own: the section is the maintenance timer's.
/// </para>
/// </summary>
internal sealed class DeletedAccountSweepOptions
{
    public bool SweepDeletedAccountsDataEnabled { get; set; } = true;
}

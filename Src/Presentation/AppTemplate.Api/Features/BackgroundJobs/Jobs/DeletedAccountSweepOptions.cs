namespace AppTemplate.Api.Features.BackgroundJobs.Jobs;

internal sealed class DeletedAccountSweepOptions
{
    public bool SweepDeletedAccountsDataEnabled { get; set; } = true;
}

namespace AppTemplate.Api.Common.Jobs;

/// <summary>
/// The role of this instance: whether it runs the periodic jobs at all.
/// <para>
/// Public because it is bound from configuration and its section name is part of the template's
/// contract with whoever deploys it. It is a deployment switch, not a job setting: each job keeps
/// its own <c>Enabled</c> and interval, and those are read only when this is true.
/// </para>
/// </summary>
public sealed class BackgroundJobsOptions
{
    /// <summary>The configuration section this type is bound from.</summary>
    public const string SectionName = "BackgroundJobs";

    /// <summary>
    /// True by default, so a single instance serves HTTP and runs every job. Set it false on the
    /// instances that should only serve HTTP once several run side by side.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

using AppTemplate.Application.Features.Files.UseCases.Commands.PurgeAbandonedRegistrations;
using AppTemplate.Application.Features.Files.UseCases.Commands.ReclaimOrphanedContent;
using Microsoft.Extensions.Options;

namespace AppTemplate.Worker.Features.Files;

/// <summary>
/// The cadence of the file feature's three background passes — its two sweeps and the inspection
/// pass — and an off switch for each. They are timed separately because they cost differently by
/// orders of magnitude, and because one of the three is a latency a user feels rather than a
/// background cost — see <c>FileBackgroundService</c> — so one interval could only ever be right for
/// one of them.
/// <para>
/// <b>There is no knob here for how far back the orphan sweep looks, and there must not be one.</b>
/// <see cref="AppTemplate.Domain.Features.Files.ValueObjects.ObjectKey.TimeSegmentFor"/> says why: a
/// file registered two years ago and deleted today has its bytes under a two-year-old prefix, so a
/// sweep restricted to recent time segments would stop being the guarantee that deleted bytes go
/// away and become a heuristic that silently leaks them. The segment makes each pass cheap and
/// ordered; it does not make the coverage narrower, and no option offered from here may.
/// </para>
/// <para>
/// There is no knob for the abandonment delay either, and that one is not this class's to offer:
/// <see cref="PurgeAbandonedRegistrationsUseCase.AbandonedAfter"/> is a property of the operation,
/// bound to the upload window a grant is minted for, and a host that could shorten it from
/// configuration could delete the registration of a client still uploading a large file over a slow
/// link. <c>FileBackgroundService</c> logs its value at start-up instead, so an operator can read
/// what it is without being able to break it.
/// </para>
/// </summary>
public sealed class FileWorkerOptions
{
    public const string SectionName = "FileWorker";

    /// <summary>
    /// One hour. <see cref="PurgeAbandonedRegistrationsUseCase"/> gives a registration a day before
    /// it may be given up on and caps a pass at 200 rows, so the cadence is what sets the drain
    /// rate: hourly clears 4 800 stale registrations a day for the price of 24 indexed queries, and
    /// a backlog larger than one pass is worked off within the same day rather than never.
    /// </summary>
    public TimeSpan PurgeAbandonedRegistrationsInterval { get; set; } = TimeSpan.FromHours(1);

    public bool PurgeAbandonedRegistrationsEnabled { get; set; } = true;

    /// <summary>
    /// Twelve hours. <see cref="ReclaimOrphanedContentUseCase"/> walks the whole store and asks the
    /// database about every page it lists, which is the most expensive thing this host does; two
    /// passes a day bound that cost. What the interval buys is only latency, and only on a path that
    /// is already covered promptly: <c>StoredFileDeletedDomainEvent</c>'s consumer reclaims the
    /// bytes of a deleted file straight away in the normal case, and this sweep is what covers the
    /// deliveries that did not happen and the deposits nothing ever announced.
    /// </summary>
    public TimeSpan ReclaimOrphanedContentInterval { get; set; } = TimeSpan.FromHours(12);

    /// <summary>
    /// Switching this off stops the only thing that guarantees the bytes of a deleted file are ever
    /// reclaimed. <c>FileBackgroundService</c> says so at warning level on every skipped pass, for
    /// that reason.
    /// </summary>
    public bool ReclaimOrphanedContentEnabled { get; set; } = true;

    /// <summary>
    /// One minute. This interval is not a cost, it is <b>the latency a user sees</b>: a deposited
    /// file is not readable until this loop has inspected it, so every second here is a second the
    /// uploader spends looking at a file that exists and cannot be opened.
    /// </summary>
    public TimeSpan InspectDepositedFilesInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// <b>Turning this off does not degrade the feature, it stops it.</b> Inspection is the only
    /// thing that moves a file from deposited to available, so with this false no upload ever
    /// becomes readable — unlike the two sweeps above, whose absence costs storage rather than
    /// function.
    /// </summary>
    public bool InspectDepositedFilesEnabled { get; set; } = true;
}

internal sealed class FileWorkerOptionsValidator : IValidateOptions<FileWorkerOptions>
{
    private static readonly TimeSpan _minimumPurgeInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _maximumPurgeInterval = TimeSpan.FromDays(1);

    /// <summary>
    /// A minute, not the second the purge allows. A pass of the orphan sweep lists the entire object
    /// store; asking for one more often than a pass can plausibly finish is not a cadence, it is a
    /// loop that never idles, and the ticks it cannot keep up with are coalesced rather than obeyed
    /// — so the value would be a lie about what the host is doing.
    /// </summary>
    private static readonly TimeSpan _minimumReclaimInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// A week. Beyond that the sweep is still correct but nobody can reason about it: an operator
    /// asking "when will these bytes be gone?" would be told "some time this month", and the storage
    /// bill is the only thing that would ever answer.
    /// </summary>
    private static readonly TimeSpan _maximumReclaimInterval = TimeSpan.FromDays(7);

    /// <summary>
    /// Ten seconds to an hour. Both ends are about the user rather than the machine: below ten
    /// seconds the loop spends its time asking an empty table, and above an hour a deposit that
    /// succeeded looks broken to whoever made it.
    /// </summary>
    private static readonly TimeSpan _minimumInspectInterval = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan _maximumInspectInterval = TimeSpan.FromHours(1);

    public ValidateOptionsResult Validate(string? name, FileWorkerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.PurgeAbandonedRegistrationsInterval < _minimumPurgeInterval
            || options.PurgeAbandonedRegistrationsInterval > _maximumPurgeInterval)
        {
            return ValidateOptionsResult.Fail(
                $"'{FileWorkerOptions.SectionName}:PurgeAbandonedRegistrationsInterval' must be between " +
                $"{_minimumPurgeInterval} and {_maximumPurgeInterval}.");
        }

        if (options.ReclaimOrphanedContentInterval < _minimumReclaimInterval
            || options.ReclaimOrphanedContentInterval > _maximumReclaimInterval)
        {
            return ValidateOptionsResult.Fail(
                $"'{FileWorkerOptions.SectionName}:ReclaimOrphanedContentInterval' must be between " +
                $"{_minimumReclaimInterval} and {_maximumReclaimInterval}.");
        }

        if (options.InspectDepositedFilesInterval < _minimumInspectInterval
            || options.InspectDepositedFilesInterval > _maximumInspectInterval)
        {
            return ValidateOptionsResult.Fail(
                $"'{FileWorkerOptions.SectionName}:InspectDepositedFilesInterval' must be between " +
                $"{_minimumInspectInterval} and {_maximumInspectInterval}.");
        }

        return ValidateOptionsResult.Success;
    }
}

using AppTemplate.Application.Features.Files.UseCases.Commands.PurgeAbandonedRegistrations;
using AppTemplate.Application.Features.Files.UseCases.Commands.ReclaimOrphanedContent;
using Microsoft.Extensions.Options;

namespace AppTemplate.Api.Features.Files.Jobs;

/// <summary>
/// The cadence of the three file passes and an off switch for each; timed separately because they
/// cost differently and inspection is a latency a user feels.
/// <para>
/// There is deliberately no option for how far back the orphan sweep looks: a file deleted today may
/// sit under a prefix years old, so a restricted sweep would stop guaranteeing reclamation. Nor for
/// the abandonment delay: <see cref="PurgeAbandonedRegistrationsUseCase.AbandonedAfter"/> is bound to
/// the upload window of a grant, and shortening it could delete a registration still uploading.
/// </para>
/// </summary>
public sealed class FileWorkerOptions
{
    public const string SectionName = "FileWorker";

    /// <summary>
    /// One hour. A pass of <see cref="PurgeAbandonedRegistrationsUseCase"/> caps at 200 rows, so the
    /// cadence sets the drain rate (4 800 a day).
    /// </summary>
    public TimeSpan PurgeAbandonedRegistrationsInterval { get; set; } = TimeSpan.FromHours(1);

    public bool PurgeAbandonedRegistrationsEnabled { get; set; } = true;

    /// <summary>
    /// Twelve hours. A pass of <see cref="ReclaimOrphanedContentUseCase"/> walks up to 250,000 keys and
    /// asks the database about every page, the costliest job here; the deletion consumer reclaims
    /// promptly in the normal case, so the interval only bounds the uncovered cases.
    /// </summary>
    public TimeSpan ReclaimOrphanedContentInterval { get; set; } = TimeSpan.FromHours(12);

    /// <summary>Off stops the only guarantee that the bytes of a deleted file are reclaimed; each skipped pass warns.</summary>
    public bool ReclaimOrphanedContentEnabled { get; set; } = true;

    /// <summary>One minute. This is the latency a user sees: a deposited file is unreadable until inspected.</summary>
    public TimeSpan InspectDepositedFilesInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Off stops the feature, not just a sweep: no upload ever becomes readable.</summary>
    public bool InspectDepositedFilesEnabled { get; set; } = true;
}

internal sealed class FileWorkerOptionsValidator : IValidateOptions<FileWorkerOptions>
{
    private static readonly TimeSpan _minimumPurgeInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _maximumPurgeInterval = TimeSpan.FromDays(1);

    /// <summary>A minute: a pass lists up to 250,000 keys, so a faster cadence would only be coalesced.</summary>
    private static readonly TimeSpan _minimumReclaimInterval = TimeSpan.FromMinutes(1);

    /// <summary>A week; beyond that nobody can say when deleted bytes will be gone.</summary>
    private static readonly TimeSpan _maximumReclaimInterval = TimeSpan.FromDays(7);

    /// <summary>Ten seconds to an hour: both ends are about the uploader's wait, not the machine.</summary>
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

using AppTemplate.Application.Common.Results;
using AppTemplate.Application.Features.Files.Errors;
using AppTemplate.Application.Features.Files.Ports.StoredFileQueries;
using AppTemplate.Domain.Features.Files.ValueObjects;

namespace AppTemplate.Application.Features.Files.Policies;

/// <summary>
/// What one owner may hold, and how many deposits they may have in flight.
/// <para>
/// <b>Without this, registering a file is a free way to mint signed upload URLs.</b> Each
/// registration hands back a bearer right to write up to <see cref="FileSize.MaxBytes"/> into the
/// bucket, and nothing else in this feature refuses one: the domain checks that a file is
/// well-formed, not that a person should be allowed another. So the rule belongs to the feature and
/// lives here, next to the collection whitelist, rather than inside an aggregate that has no way to
/// see its owner's other files.
/// </para>
/// <para>
/// The three bounds measure three different costs, which is why there are three rather than one.
/// </para>
/// </summary>
public static class StoredFileQuotaPolicy
{
    /// <summary>
    /// How many uploads one owner may have outstanding at once — the anti-abuse bound, and the
    /// tightest of the three.
    /// <para>
    /// A pending registration costs a reserved key and no bytes, so this is not about storage. It is
    /// about how many unexpired write rights one caller may be holding: each is a URL that can be
    /// used once, from anywhere, by anyone who has it, for as long as it lasts. A client that
    /// deposits and confirms as it goes never comes near twenty, and one that registers in a loop
    /// without ever depositing stops here rather than at the byte ceiling.
    /// </para>
    /// <para>
    /// It is also self-clearing: <c>PurgeAbandonedRegistrationsUseCase</c> removes registrations
    /// that were never deposited against, so a client that crashed mid-upload is not locked out
    /// permanently — it waits.
    /// </para>
    /// </summary>
    public const int MaxPendingRegistrations = 20;

    /// <summary>
    /// How many files one owner may keep. Bounds the read side rather than the store: every page of
    /// their listing, and the quota count above, are queries whose cost grows with this number.
    /// Deliberately generous next to the pending bound, because a confirmed file is a thing the user
    /// asked for and paid for with an actual upload.
    /// </summary>
    public const int MaxFiles = 1_000;

    /// <summary>
    /// 10 GiB, the only bound that measures money. Sized so that a single owner cannot fill a bucket
    /// on their own — at <see cref="FileSize.MaxBytes"/> per file it is two files, which is the
    /// point: the byte ceiling and the file ceiling bind different shapes of abuse, and neither
    /// implies the other.
    /// </summary>
    public const long MaxBytes = 10L * 1024 * 1024 * 1024;

    /// <summary>
    /// Whether one more file of <paramref name="declaredSizeInBytes"/> fits.
    /// <para>
    /// Measured against <see cref="OwnerStorageUsage.CommittedBytes"/>, so bytes an owner has merely
    /// promised count as if they were already stored. Counting only confirmed bytes would let a
    /// caller register a thousand five-gigabyte files, pass every check, and then deposit them all.
    /// </para>
    /// <para>
    /// <b>This is a check, not a reservation.</b> Two registrations racing can both read the same
    /// usage and both be allowed, so the true ceiling is the bound plus one request's worth. That is
    /// accepted deliberately: making it exact would mean serialising every registration against a
    /// per-owner lock, for a rule whose job is to stop unbounded abuse rather than to bill anyone.
    /// </para>
    /// </summary>
    public static Result EnsureRoomFor(OwnerStorageUsage usage, long declaredSizeInBytes)
    {
        ArgumentNullException.ThrowIfNull(usage);

        if (usage.PendingCount >= MaxPendingRegistrations)
        {
            return Result.Failure(StoredFileErrors.QuotaExceeded(
                $"You already have {MaxPendingRegistrations} uploads waiting to be completed. " +
                "Finish or abandon one before starting another."));
        }

        if (usage.TotalCount >= MaxFiles)
        {
            return Result.Failure(StoredFileErrors.QuotaExceeded(
                $"You have reached the limit of {MaxFiles} files. Delete one before adding another."));
        }

        if (usage.CommittedBytes + declaredSizeInBytes > MaxBytes)
        {
            return Result.Failure(StoredFileErrors.QuotaExceeded(
                $"This file would take you past your {MaxBytes} byte allowance."));
        }

        return Result.Success();
    }
}

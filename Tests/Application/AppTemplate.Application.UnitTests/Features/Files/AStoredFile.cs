using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Common.Abstractions;
using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Domain.Features.Files.ValueObjects;

namespace AppTemplate.Application.UnitTests.Features.Files;

/// <summary>
/// Builds real aggregates, never fakes — same rationale as <c>ATodoList</c> and <c>AReminder</c>: a
/// root that accepted anything would make "a mismatched deposit is refused" assert nothing.
/// </summary>
/// <remarks>
/// It sits here rather than beside those two in <c>TestDoubles/</c> only because this agent's scope
/// was the <c>Files</c> folder. Its home is <c>TestDoubles/AStoredFile.cs</c>.
/// </remarks>
internal static class AStoredFile
{
    /// <summary>SHA-256 of "test". Any 64 hexadecimal characters would do; a real digest is used so
    /// nothing reads as a placeholder that might be checked one day.</summary>
    internal const string Checksum = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    /// <summary>A different real digest, for asserting that a mismatch is a mismatch.</summary>
    internal const string OtherChecksum = "60303ae22b998861bce3b28f33eec1be758a213c86c93c076dbe9f558c11c752";

    internal const long SizeInBytes = 1_024;

    internal const string MediaType = "image/png";

    /// <summary>A registration exactly as <see cref="StoredFile.Register"/> makes one: pending, with
    /// a key reserved and nothing deposited.</summary>
    internal static StoredFile PendingOwnedBy(
        Guid ownerId,
        DateTimeOffset? registeredAt = null,
        string name = "holiday.png",
        long sizeInBytes = SizeInBytes,
        string checksum = Checksum) =>
        StoredFile.Register(
            ownerId,
            StoredFileName.Create(name),
            DeclaredMediaType.Create(MediaType),
            FileSize.Create(sizeInBytes),
            Sha256Checksum.Create(checksum),
            registeredAt ?? StubDateTimeProvider.DefaultInstant);

    internal static StoredFile PendingOwnedBySomebodyElseThan(Guid notThisUserId) =>
        PendingOwnedBy(AnotherOwnerThan(notThisUserId), name: "somebody else's file.png");

    /// <summary>
    /// Rehydrated straight into <see cref="StoredFileState.Available"/>, the way a store loads one:
    /// <see cref="StoredFile.Register"/> cannot produce a confirmed file, since confirming needs the
    /// store to have reported what it holds.
    /// </summary>
    internal static StoredFile AvailableOwnedBy(
        Guid ownerId,
        long sizeInBytes = SizeInBytes,
        string checksum = Checksum) =>
        StoredFile.Rehydrate(
            Guid.CreateVersion7(),
            ownerId,
            ObjectKey.New(StubDateTimeProvider.DefaultInstant),
            StoredFileName.Create("holiday.png"),
            DeclaredMediaType.Create(MediaType),
            FileSize.Create(sizeInBytes),
            Sha256Checksum.Create(checksum),
            StoredFileState.Available,
            StubDateTimeProvider.DefaultInstant,
            StubDateTimeProvider.DefaultInstant.AddMinutes(1));

    internal static StoredFile AvailableOwnedBySomebodyElseThan(Guid notThisUserId) =>
        AvailableOwnedBy(AnotherOwnerThan(notThisUserId));

    /// <summary>
    /// A file whose deposit has been confirmed and whose content has not been examined: what the
    /// inspection pass loads, and what a client polling for its upload sees in the meantime.
    /// </summary>
    internal static StoredFile DepositedOwnedBy(
        Guid ownerId,
        DateTimeOffset? registeredAt = null,
        string mediaType = MediaType)
    {
        var storedFile = StoredFile.Register(
            ownerId,
            StoredFileName.Create("holiday.png"),
            DeclaredMediaType.Create(mediaType),
            FileSize.Create(SizeInBytes),
            Sha256Checksum.Create(Checksum),
            registeredAt ?? StubDateTimeProvider.DefaultInstant);

        storedFile.ConfirmDeposit(FileSize.Create(SizeInBytes), Sha256Checksum.Create(Checksum));

        return storedFile;
    }

    /// <summary>
    /// A file whose content was examined and refused, rehydrated the way a store loads one — and
    /// therefore with no availability instant, which the aggregate refuses to load one without.
    /// </summary>
    internal static StoredFile QuarantinedOwnedBy(Guid ownerId) =>
        StoredFile.Rehydrate(
            Guid.CreateVersion7(),
            ownerId,
            ObjectKey.New(StubDateTimeProvider.DefaultInstant),
            StoredFileName.Create("holiday.png"),
            DeclaredMediaType.Create(MediaType),
            FileSize.Create(SizeInBytes),
            Sha256Checksum.Create(Checksum),
            StoredFileState.Quarantined,
            StubDateTimeProvider.DefaultInstant,
            null);

    /// <summary>Placed at <paramref name="version"/> the way the store places a freshly loaded
    /// aggregate. Goes through <see cref="IVersioned"/> because that is the only way anything writes
    /// a version.</summary>
    internal static StoredFile AvailableOwnedByAtVersion(Guid ownerId, uint version)
    {
        var storedFile = AvailableOwnedBy(ownerId);
        ((IVersioned)storedFile).SetVersion(version);

        return storedFile;
    }

    private static Guid AnotherOwnerThan(Guid notThisUserId)
    {
        var otherOwnerId = Guid.CreateVersion7();

        if (otherOwnerId == notThisUserId)
        {
            throw new InvalidOperationException("Guid.CreateVersion7 produced a collision.");
        }

        return otherOwnerId;
    }
}

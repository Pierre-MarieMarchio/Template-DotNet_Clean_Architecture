using AppTemplate.Application.UnitTests.TestDoubles;
using AppTemplate.Domain.Core.Common.Abstractions;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;

namespace AppTemplate.Application.UnitTests.Features.Files;

internal static class AStoredFile
{
    internal const string Checksum = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    internal const string OtherChecksum = "60303ae22b998861bce3b28f33eec1be758a213c86c93c076dbe9f558c11c752";

    internal const long SizeInBytes = 1_024;

    internal const string MediaType = "image/png";

    internal const string Category = FilesSettings.Document;

    internal static StoredFile PendingOwnedBy(
        UserId ownerId,
        DateTimeOffset? registeredAt = null,
        string name = "holiday.png",
        long sizeInBytes = SizeInBytes,
        string checksum = Checksum,
        string category = Category) =>
        StoredFile.Register(
            ownerId,
            FileCategory.Create(category),
            StoredFileDeclaration.Create(
                StoredFileName.Create(name),
                DeclaredMediaType.Create(MediaType),
                FileSize.Create(sizeInBytes),
                Sha256Checksum.Create(checksum)),
            registeredAt ?? StubDateTimeProvider.DefaultInstant);

    internal static StoredFile PendingOwnedBySomebodyElseThan(UserId notThisUserId) =>
        PendingOwnedBy(AnotherOwnerThan(notThisUserId), name: "somebody else's file.png");

    internal static StoredFile AvailableOwnedBy(
        UserId ownerId,
        long sizeInBytes = SizeInBytes,
        string checksum = Checksum,
        string category = Category) =>
        StoredFile.Rehydrate(new StoredFileSnapshot
        {
            Id = Guid.CreateVersion7(),
            OwnerId = ownerId,
            Category = FileCategory.Create(category),
            ObjectKey = ObjectKey.New(StubDateTimeProvider.DefaultInstant),
            Name = StoredFileName.Create("holiday.png"),
            DeclaredMediaType = DeclaredMediaType.Create(MediaType),
            Size = FileSize.Create(sizeInBytes),
            Checksum = Sha256Checksum.Create(checksum),
            State = StoredFileState.Available,
            RegisteredAt = StubDateTimeProvider.DefaultInstant,
            AvailableAt = StubDateTimeProvider.DefaultInstant.AddMinutes(1),
            InspectionAttempts = 0,
            NextInspectionAt = null,
            Tags = [],
        });

    internal static StoredFile AvailableOwnedBySomebodyElseThan(UserId notThisUserId) =>
        AvailableOwnedBy(AnotherOwnerThan(notThisUserId));

    internal static StoredFile DepositedOwnedBy(
        UserId ownerId,
        DateTimeOffset? registeredAt = null,
        string mediaType = MediaType,
        string category = Category)
    {
        var storedFile = StoredFile.Register(
            ownerId,
            FileCategory.Create(category),
            StoredFileDeclaration.Create(
                StoredFileName.Create("holiday.png"),
                DeclaredMediaType.Create(mediaType),
                FileSize.Create(SizeInBytes),
                Sha256Checksum.Create(Checksum)),
            registeredAt ?? StubDateTimeProvider.DefaultInstant);

        storedFile.ConfirmDeposit(FileSize.Create(SizeInBytes), Sha256Checksum.Create(Checksum));

        return storedFile;
    }

    internal static StoredFile QuarantinedOwnedBy(UserId ownerId) =>
        StoredFile.Rehydrate(new StoredFileSnapshot
        {
            Id = Guid.CreateVersion7(),
            OwnerId = ownerId,
            Category = FileCategory.Create(Category),
            ObjectKey = ObjectKey.New(StubDateTimeProvider.DefaultInstant),
            Name = StoredFileName.Create("holiday.png"),
            DeclaredMediaType = DeclaredMediaType.Create(MediaType),
            Size = FileSize.Create(SizeInBytes),
            Checksum = Sha256Checksum.Create(Checksum),
            State = StoredFileState.Quarantined,
            RegisteredAt = StubDateTimeProvider.DefaultInstant,
            AvailableAt = null,
            InspectionAttempts = 0,
            NextInspectionAt = null,
            Tags = [],
        });

    internal static StoredFile AvailableOwnedByAtVersion(UserId ownerId, uint version)
    {
        var storedFile = AvailableOwnedBy(ownerId);
        ((IVersioned)storedFile).SetVersion(version);

        return storedFile;
    }

    private static UserId AnotherOwnerThan(UserId notThisUserId)
    {
        var otherOwnerId = UserId.Create(Guid.CreateVersion7());

        if (otherOwnerId == notThisUserId)
        {
            throw new InvalidOperationException("Guid.CreateVersion7 produced a collision.");
        }

        return otherOwnerId;
    }
}

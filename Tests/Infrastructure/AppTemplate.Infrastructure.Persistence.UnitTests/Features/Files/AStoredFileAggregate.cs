using AppTemplate.Domain.Core.Common.Abstractions;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.Files;

internal static class AStoredFileAggregate
{
    internal static readonly UserId OwnerId = UserId.Create(new("4b7f1d92-4c8a-4f4b-9a1e-0d2f3c4b5a71"));
    internal static readonly Guid CreatedBy = new("11111111-2222-3333-4444-555555555557");
    internal static readonly Guid LastModifiedBy = new("66666666-7777-8888-9999-aaaaaaaaaaac");

    internal static readonly DateTimeOffset RegisteredAt = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);
    internal static readonly DateTimeOffset AvailableAt = new(2026, 6, 1, 9, 4, 30, TimeSpan.Zero);
    internal static readonly DateTimeOffset NextInspectionAt = new(2026, 6, 1, 9, 2, 0, TimeSpan.Zero);
    internal static readonly DateTimeOffset CreatedAt = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
    internal static readonly DateTimeOffset LastModifiedAt = new(2026, 3, 5, 8, 9, 10, TimeSpan.Zero);

    internal const string ObjectKeyValue = "t0/202606/0123456789abcdef0123456789abcdef";

    internal const string CategoryName = "invoices";
    internal const string OtherCategoryName = "contracts";
    internal const string NameValue = "quarterly-report.pdf";
    internal const string DeclaredMediaTypeValue = "application/pdf";
    internal const long SizeInBytes = 4_194_304L;
    internal const string ChecksumValue = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    internal const uint Version = 987_654u;

    internal const int InspectionAttempts = 3;

    internal const StoredFileState State = StoredFileState.Available;

    internal static Guid StoredFileId { get; } = new("0199a3c4-5555-7000-8000-000000000099");

    internal static StoredFile FullyPopulated()
    {
        var aggregate = StoredFile.Rehydrate(new StoredFileSnapshot
        {
            Id = StoredFileId,
            OwnerId = OwnerId,
            Category = FileCategory.Create(CategoryName),
            ObjectKey = ObjectKey.Create(ObjectKeyValue),
            Name = StoredFileName.Create(NameValue),
            DeclaredMediaType = DeclaredMediaType.Create(DeclaredMediaTypeValue),
            Size = FileSize.Create(SizeInBytes),
            Checksum = Sha256Checksum.Create(ChecksumValue),
            State = State,
            RegisteredAt = RegisteredAt,
            AvailableAt = AvailableAt,
            InspectionAttempts = InspectionAttempts,
            NextInspectionAt = NextInspectionAt,
            Tags = ["invoice", "2026"],
        });

        ((IVersioned)aggregate).SetVersion(Version);
        ((IAuditable)aggregate).SetCreated(CreatedAt, CreatedBy);
        ((IAuditable)aggregate).SetLastModified(LastModifiedAt, LastModifiedBy);

        return aggregate;
    }

    internal static StoredFile DifferentInEveryDomainOwnedValue()
    {
        var aggregate = StoredFile.Rehydrate(new StoredFileSnapshot
        {
            Id = StoredFileId,
            OwnerId = OtherOwnerId,
            Category = FileCategory.Create(OtherCategoryName),
            ObjectKey = ObjectKey.Create(OtherObjectKeyValue),
            Name = StoredFileName.Create(OtherNameValue),
            DeclaredMediaType = DeclaredMediaType.Create(OtherDeclaredMediaTypeValue),
            Size = FileSize.Create(OtherSizeInBytes),
            Checksum = Sha256Checksum.Create(OtherChecksumValue),
            State = OtherState,
            RegisteredAt = OtherRegisteredAt,
            AvailableAt = OtherAvailableAt,
            InspectionAttempts = OtherInspectionAttempts,
            NextInspectionAt = OtherNextInspectionAt,
            Tags = ["receipt"],
        });

        ((IVersioned)aggregate).SetVersion(OtherVersion);
        ((IAuditable)aggregate).SetCreated(OtherCreatedAt, OtherCreatedBy);
        ((IAuditable)aggregate).SetLastModified(OtherLastModifiedAt, OtherLastModifiedBy);

        return aggregate;
    }

    internal static readonly UserId OtherOwnerId = UserId.Create(new("7c1e2d3f-4a5b-4c6d-8e9f-0a1b2c3d4e71"));
    internal static readonly Guid OtherCreatedBy = new("22222222-3333-4444-5555-666666666668");
    internal static readonly Guid OtherLastModifiedBy = new("77777777-8888-9999-aaaa-bbbbbbbbbbbd");

    internal static readonly DateTimeOffset OtherRegisteredAt = new(2025, 7, 8, 9, 10, 11, TimeSpan.Zero);
    internal static readonly DateTimeOffset OtherCreatedAt = new(2025, 7, 8, 9, 10, 11, TimeSpan.Zero);
    internal static readonly DateTimeOffset OtherLastModifiedAt = new(2025, 7, 9, 12, 13, 14, TimeSpan.Zero);

    internal const string OtherObjectKeyValue = "t0/202507/fedcba9876543210fedcba9876543210";
    internal const string OtherNameValue = "holiday-photo.jpeg";
    internal const string OtherDeclaredMediaTypeValue = "image/jpeg";
    internal const long OtherSizeInBytes = 1_048_576L;
    internal const string OtherChecksumValue = "5f70bf18a086007016e948b04aed3b82103a36bea41755b6cddfaf10ace3c6ef";

    internal const uint OtherVersion = 123_456u;

    internal const StoredFileState OtherState = StoredFileState.Pending;

    internal static DateTimeOffset? OtherAvailableAt => null;

    internal const int OtherInspectionAttempts = 1;

    internal static readonly DateTimeOffset OtherNextInspectionAt = new(2025, 7, 8, 9, 11, 11, TimeSpan.Zero);
}

using AppTemplate.Domain.Common.Abstractions;
using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Domain.Features.Files.ValueObjects;

namespace AppTemplate.Infrastructure.Persistence.UnitTests.Features.Files;

/// <summary>
/// Builds a stored-file aggregate in which <b>every</b> piece of state is set to a value distinguishable
/// from its type's default.
/// </summary>
/// <remarks>
/// A round-trip assertion that compares <c>null</c> against <c>null</c>, or the enum's default member
/// against itself, passes for a property the mapper never copied — so a sample built from a file that
/// had only just been registered would silently exempt exactly the properties confirmation sets: a
/// non-<c>Pending</c> <c>State</c> and an <c>AvailableAt</c>. This builder goes through
/// <see cref="StoredFile.Rehydrate"/> rather than the lifecycle methods, so it can put the aggregate
/// straight into that confirmed shape without a clock to advance.
/// </remarks>
internal static class AStoredFileAggregate
{
    internal static readonly Guid OwnerId = new("4b7f1d92-4c8a-4f4b-9a1e-0d2f3c4b5a71");
    internal static readonly Guid CreatedBy = new("11111111-2222-3333-4444-555555555557");
    internal static readonly Guid LastModifiedBy = new("66666666-7777-8888-9999-aaaaaaaaaaac");

    internal static readonly DateTimeOffset RegisteredAt = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);
    internal static readonly DateTimeOffset AvailableAt = new(2026, 6, 1, 9, 4, 30, TimeSpan.Zero);
    internal static readonly DateTimeOffset CreatedAt = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
    internal static readonly DateTimeOffset LastModifiedAt = new(2026, 3, 5, 8, 9, 10, TimeSpan.Zero);

    /// <summary>
    /// Written out rather than minted through <see cref="ObjectKey.New"/>, and that is the point: the
    /// tests that use it assert an exact string, and a key drawn from a cryptographic generator would
    /// make "the key survived" and "the key was regenerated" indistinguishable. Its time segment matches
    /// <see cref="RegisteredAt"/>, exactly as a real mint would leave it.
    /// </summary>
    internal const string ObjectKeyValue = "t0/202606/0123456789abcdef0123456789abcdef";

    internal const string NameValue = "quarterly-report.pdf";
    internal const string DeclaredMediaTypeValue = "application/pdf";
    internal const long SizeInBytes = 4_194_304L;
    internal const string ChecksumValue = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    /// <summary>Non-zero, so a mapper that dropped the concurrency token is visible.</summary>
    internal const uint Version = 987_654u;

    /// <summary>Not <see cref="StoredFileState.Pending"/>, the enum's default member.</summary>
    internal const StoredFileState State = StoredFileState.Available;

    internal static Guid StoredFileId { get; } = new("0199a3c4-5555-7000-8000-000000000099");

    /// <summary>A fully populated aggregate, as if it had just been loaded after confirmation.</summary>
    internal static StoredFile FullyPopulated()
    {
        var aggregate = StoredFile.Rehydrate(
            StoredFileId,
            OwnerId,
            ObjectKey.Create(ObjectKeyValue),
            StoredFileName.Create(NameValue),
            DeclaredMediaType.Create(DeclaredMediaTypeValue),
            FileSize.Create(SizeInBytes),
            Sha256Checksum.Create(ChecksumValue),
            State,
            RegisteredAt,
            AvailableAt);

        ((IVersioned)aggregate).SetVersion(Version);
        ((IAuditable)aggregate).SetCreated(CreatedAt, CreatedBy);
        ((IAuditable)aggregate).SetLastModified(LastModifiedAt, LastModifiedBy);

        return aggregate;
    }

    /// <summary>
    /// The same file, with <b>every</b> domain-owned value different from <see cref="FullyPopulated"/>.
    /// The id is deliberately unchanged: a different id would make this a fresh insert, going through
    /// <c>ToNewRecord</c> instead of the update path this sample exists to exercise.
    /// </summary>
    /// <remarks>
    /// No operation on the aggregate moves the owner, the key or the registration instant — a file does
    /// not change hands and its bytes do not move. They differ here anyway, because these samples exist
    /// to prove <c>WriteTo</c> is <em>total</em>: a column left out of it is a column a future operation
    /// could move without the write ever landing, and "nothing changes it today" is not a property a
    /// test can check.
    /// </remarks>
    internal static StoredFile DifferentInEveryDomainOwnedValue()
    {
        var aggregate = StoredFile.Rehydrate(
            StoredFileId,
            OtherOwnerId,
            ObjectKey.Create(OtherObjectKeyValue),
            StoredFileName.Create(OtherNameValue),
            DeclaredMediaType.Create(OtherDeclaredMediaTypeValue),
            FileSize.Create(OtherSizeInBytes),
            Sha256Checksum.Create(OtherChecksumValue),
            OtherState,
            OtherRegisteredAt,
            OtherAvailableAt);

        ((IVersioned)aggregate).SetVersion(OtherVersion);
        ((IAuditable)aggregate).SetCreated(OtherCreatedAt, OtherCreatedBy);
        ((IAuditable)aggregate).SetLastModified(OtherLastModifiedAt, OtherLastModifiedBy);

        return aggregate;
    }

    // ---- The second, entirely different set of values -------------------------------------------

    internal static readonly Guid OtherOwnerId = new("7c1e2d3f-4a5b-4c6d-8e9f-0a1b2c3d4e71");
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

    /// <summary>
    /// Pending rather than Available, so it differs from <see cref="State"/>. Rehydrate ties the
    /// confirmation instant to that state — only a confirmed file carries one — so this sample
    /// necessarily carries none, which is also how it differs from <see cref="AvailableAt"/>.
    /// </summary>
    internal const StoredFileState OtherState = StoredFileState.Pending;

    internal static DateTimeOffset? OtherAvailableAt => null;
}

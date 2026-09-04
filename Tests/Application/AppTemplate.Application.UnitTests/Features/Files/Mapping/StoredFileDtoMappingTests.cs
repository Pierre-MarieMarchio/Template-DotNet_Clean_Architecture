using AppTemplate.Application.Features.Files.Mapping;
using AppTemplate.Domain.Common.Abstractions;
using AppTemplate.Domain.Features.Files.ValueObjects;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.Mapping;

public sealed class StoredFileDtoMappingTests
{
    private static readonly Guid _ownerId = Guid.CreateVersion7();

    [Fact]
    public void EveryFieldOfTheAggregate_ReachesTheDto()
    {
        var storedFile = AStoredFile.AvailableOwnedBy(_ownerId);

        var dto = StoredFileDtoMapping.ToDto(storedFile);

        dto.Id.ShouldBe(storedFile.Id);
        dto.Name.ShouldBe(storedFile.Name.Value);
        dto.DeclaredMediaType.ShouldBe(storedFile.DeclaredMediaType.Value);
        dto.SizeInBytes.ShouldBe(storedFile.Size.Bytes);
        dto.Checksum.ShouldBe(storedFile.Checksum.Value);
        dto.State.ShouldBe(StoredFileState.Available);
        dto.RegisteredAt.ShouldBe(storedFile.RegisteredAt);
        dto.AvailableAt.ShouldBe(storedFile.AvailableAt);
    }

    /// <summary>
    /// The key never leaves the application layer. It names the bytes, and a client holding one is a
    /// client that can start guessing at the bucket policy in front of them.
    /// </summary>
    [Fact]
    public void TheObjectKey_DoesNotReachTheDto()
    {
        var storedFile = AStoredFile.AvailableOwnedBy(_ownerId);

        var dto = StoredFileDtoMapping.ToDto(storedFile);

        dto.ToString().ShouldNotContain(storedFile.ObjectKey.Value);
    }

    /// <summary>The owner is not in the DTO either: every read is already scoped to one.</summary>
    [Fact]
    public void TheOwner_DoesNotReachTheDto()
    {
        var storedFile = AStoredFile.AvailableOwnedBy(_ownerId);

        StoredFileDtoMapping.ToDto(storedFile).ToString()
            .ShouldNotContain(_ownerId.ToString());
    }

    [Fact]
    public void APendingFile_HasNoConfirmationInstant()
    {
        var dto = StoredFileDtoMapping.ToDto(AStoredFile.PendingOwnedBy(_ownerId));

        dto.State.ShouldBe(StoredFileState.Pending);
        dto.AvailableAt.ShouldBeNull();
    }

    /// <summary>
    /// The version travels beside the value rather than inside it, so a caller publishing a
    /// validator has it and the DTO stays a description of the resource.
    /// </summary>
    [Fact]
    public void TheVersion_TravelsBesideTheValue()
    {
        var storedFile = AStoredFile.AvailableOwnedBy(_ownerId);
        ((IVersioned)storedFile).SetVersion(42);

        var versioned = StoredFileDtoMapping.ToVersioned(storedFile);

        versioned.Version.ShouldBe(42u);
        versioned.Value.Id.ShouldBe(storedFile.Id);
    }

    [Fact]
    public void ANullAggregate_IsARejectedArgument() =>
        Should.Throw<ArgumentNullException>(() => StoredFileDtoMapping.ToDto(null!));
}

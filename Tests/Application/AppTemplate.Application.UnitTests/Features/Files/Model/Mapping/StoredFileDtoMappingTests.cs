using AppTemplate.Application.Features.Files.Model.Mapping;
using AppTemplate.Domain.Core.Common.Abstractions;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using Shouldly;
using Xunit;

namespace AppTemplate.Application.UnitTests.Features.Files.Model.Mapping;

public sealed class StoredFileDtoMappingTests
{
    private static readonly UserId _ownerId = UserId.Create(Guid.CreateVersion7());

    [Fact]
    public void EveryFieldOfTheAggregate_ReachesTheDto()
    {
        var storedFile = AStoredFile.AvailableOwnedBy(_ownerId);

        var dto = StoredFileDtoMapping.ToDto(storedFile);

        dto.Id.ShouldBe(storedFile.Id);
        dto.Category.ShouldBe(storedFile.Category.Value);
        dto.Name.ShouldBe(storedFile.Name.Value);
        dto.DeclaredMediaType.ShouldBe(storedFile.DeclaredMediaType.Value);
        dto.SizeInBytes.ShouldBe(storedFile.Size.Bytes);
        dto.Checksum.ShouldBe(storedFile.Checksum.Value);
        dto.State.ShouldBe(StoredFileState.Available);
        dto.RegisteredAt.ShouldBe(storedFile.RegisteredAt);
        dto.AvailableAt.ShouldBe(storedFile.AvailableAt);
    }

    [Fact]
    public void TheObjectKey_DoesNotReachTheDto()
    {
        var storedFile = AStoredFile.AvailableOwnedBy(_ownerId);

        var dto = StoredFileDtoMapping.ToDto(storedFile);

        dto.ToString().ShouldNotContain(storedFile.ObjectKey.Value);
    }

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

    [Fact]
    public void TheVersion_TravelsBesideTheValue()
    {
        var storedFile = AStoredFile.AvailableOwnedBy(_ownerId);
        ((IVersioned)storedFile).SetVersion(42);

        var versioned = StoredFileDtoMapping.ToVersioned(storedFile);

        versioned.Version.ShouldBe(42u);
        versioned.Value.Id.ShouldBe(storedFile.Id);
    }
}

using AppTemplate.Domain.Core.Common.Exceptions;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.UnitTests.Features.Files.Model.Entities;

public sealed class StoredFileRehydrationTests
{
    private static readonly DateTimeOffset _registeredAt = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly UserId _ownerId = UserId.Create(Guid.CreateVersion7());

    private static readonly ObjectKey _objectKey =
        ObjectKey.Create("t0/202608/0123456789abcdef0123456789abcdef");

    private static readonly FileCategory _category = FileCategory.Create("document");
    private static readonly StoredFileName _name = StoredFileName.Create("quarterly-report.pdf");
    private static readonly DeclaredMediaType _mediaType = DeclaredMediaType.Create("application/pdf");
    private static readonly FileSize _size = FileSize.Create(4096);

    private static readonly Sha256Checksum _checksum =
        Sha256Checksum.Create("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");

    private static StoredFile Rehydrate(
        Guid? id = null,
        StoredFileState state = StoredFileState.Pending,
        DateTimeOffset? registeredAt = null,
        DateTimeOffset? availableAt = null,
        int inspectionAttempts = 0,
        DateTimeOffset? nextInspectionAt = null,
        IEnumerable<string>? tags = null) =>
        StoredFile.Rehydrate(new StoredFileSnapshot
        {
            Id = id ?? Guid.CreateVersion7(),
            OwnerId = _ownerId,
            Category = _category,
            ObjectKey = _objectKey,
            Name = _name,
            DeclaredMediaType = _mediaType,
            Size = _size,
            Checksum = _checksum,
            State = state,
            RegisteredAt = registeredAt ?? _registeredAt,
            AvailableAt = availableAt,
            InspectionAttempts = inspectionAttempts,
            NextInspectionAt = nextInspectionAt,
            Tags = tags ?? [],
        });

    #region What a stored row must carry

    [Fact]
    public void Rehydrate_Rejects_AnAbsentSnapshot() =>
        Should.Throw<ArgumentNullException>(() => StoredFile.Rehydrate(null!));

    [Fact]
    public void Rehydrate_Rejects_AnAbsentTagSequence() =>
        Should.Throw<ArgumentNullException>(
            () => StoredFile.Rehydrate(new StoredFileSnapshot
            {
                Id = Guid.CreateVersion7(),
                OwnerId = _ownerId,
                Category = _category,
                ObjectKey = _objectKey,
                Name = _name,
                DeclaredMediaType = _mediaType,
                Size = _size,
                Checksum = _checksum,
                State = StoredFileState.Pending,
                RegisteredAt = _registeredAt,
                AvailableAt = null,
                InspectionAttempts = 0,
                NextInspectionAt = null,
                Tags = null!,
            }));

    [Fact]
    public void Rehydrate_Rejects_AnEmptyId()
    {
        var exception = Should.Throw<DomainException>(() => Rehydrate(id: Guid.Empty));

        exception.Message.ShouldContain("id");
    }

    [Fact]
    public void Rehydrate_Rejects_AnAbsentOwner() =>
        Should.Throw<ArgumentNullException>(
            () => StoredFile.Rehydrate(new StoredFileSnapshot
            {
                Id = Guid.CreateVersion7(),
                OwnerId = null!,
                Category = _category,
                ObjectKey = _objectKey,
                Name = _name,
                DeclaredMediaType = _mediaType,
                Size = _size,
                Checksum = _checksum,
                State = StoredFileState.Pending,
                RegisteredAt = _registeredAt,
                AvailableAt = null,
                InspectionAttempts = 0,
                NextInspectionAt = null,
                Tags = [],
            }));

    [Fact]
    public void Rehydrate_Rejects_AnAbsentCategory() =>
        Should.Throw<ArgumentNullException>(
            () => StoredFile.Rehydrate(new StoredFileSnapshot
            {
                Id = Guid.CreateVersion7(),
                OwnerId = _ownerId,
                Category = null!,
                ObjectKey = _objectKey,
                Name = _name,
                DeclaredMediaType = _mediaType,
                Size = _size,
                Checksum = _checksum,
                State = StoredFileState.Pending,
                RegisteredAt = _registeredAt,
                AvailableAt = null,
                InspectionAttempts = 0,
                NextInspectionAt = null,
                Tags = [],
            }));

    [Fact]
    public void Rehydrate_Rejects_AMissingRegistrationInstant() =>
        Should.Throw<DomainException>(() => Rehydrate(registeredAt: default(DateTimeOffset)));

    [Fact]
    public void Rehydrate_Rejects_ANullValueObject() =>
        Should.Throw<ArgumentNullException>(
            () => StoredFile.Rehydrate(new StoredFileSnapshot
            {
                Id = Guid.CreateVersion7(),
                OwnerId = _ownerId,
                Category = _category,
                ObjectKey = null!,
                Name = _name,
                DeclaredMediaType = _mediaType,
                Size = _size,
                Checksum = _checksum,
                State = StoredFileState.Pending,
                RegisteredAt = _registeredAt,
                AvailableAt = null,
                InspectionAttempts = 0,
                NextInspectionAt = null,
                Tags = [],
            }));

    #endregion

    #region Rows that contradict themselves

    [Fact]
    public void Rehydrate_Rejects_AnAvailableFileWithNoAvailabilityInstant()
    {
        var exception = Should.Throw<DomainException>(
            () => Rehydrate(state: StoredFileState.Available, availableAt: null));

        exception.Message.ShouldContain("must record");
    }

    [Theory]
    [InlineData(StoredFileState.Pending)]
    [InlineData(StoredFileState.Deposited)]
    [InlineData(StoredFileState.Quarantined)]
    public void Rehydrate_Rejects_AFileThatIsNotAvailableHoldingAnAvailabilityInstant(StoredFileState state)
    {
        var exception = Should.Throw<DomainException>(
            () => Rehydrate(state: state, availableAt: _registeredAt.AddMinutes(1)));

        exception.Message.ShouldContain("Only an available");
    }

    [Theory]
    [InlineData(StoredFileState.Pending)]
    [InlineData(StoredFileState.Deposited)]
    [InlineData(StoredFileState.Quarantined)]
    public void Rehydrate_Accepts_AFileThatIsNotAvailableWithNoInstant(StoredFileState state) =>
        Rehydrate(state: state).State.ShouldBe(state);

    #endregion

    #region Rows that only look contradictory

    [Fact]
    public void Rehydrate_DoesNotCheckThatTheConfirmationFollowsTheRegistration() =>
        Should.NotThrow(
            () => Rehydrate(
                state: StoredFileState.Available,
                availableAt: _registeredAt.AddMinutes(-5)));

    #endregion

    #region Inspection retry state

    [Fact]
    public void Rehydrate_Rejects_ANegativeNumberOfInspectionAttempts() =>
        Should.Throw<DomainException>(
            () => Rehydrate(state: StoredFileState.Deposited, inspectionAttempts: -1));

    [Fact]
    public void Rehydrate_Rejects_ANextInspectionWithoutAnyAttempt() =>
        Should.Throw<DomainException>(
            () => Rehydrate(state: StoredFileState.Deposited, nextInspectionAt: _registeredAt.AddMinutes(1)));

    [Fact]
    public void Rehydrate_RestoresADeferredInspection()
    {
        var file = Rehydrate(
            state: StoredFileState.Deposited,
            inspectionAttempts: 2,
            nextInspectionAt: _registeredAt.AddMinutes(3));

        file.InspectionAttempts.ShouldBe(2);
        file.NextInspectionAt.ShouldBe(_registeredAt.AddMinutes(3));
    }

    [Fact]
    public void Rehydrate_Accepts_AnAvailableFileThatWasDeferredBeforeItsVerdict() =>
        Should.NotThrow(
            () => Rehydrate(
                state: StoredFileState.Available,
                availableAt: _registeredAt.AddMinutes(5),
                inspectionAttempts: 1,
                nextInspectionAt: _registeredAt.AddMinutes(2)));

    #endregion

    #region What a loaded file is

    [Fact]
    public void Rehydrate_RestoresTheStoredIdentityAndValues()
    {
        var id = Guid.CreateVersion7();
        var availableAt = _registeredAt.AddMinutes(1);

        var file = StoredFile.Rehydrate(new StoredFileSnapshot
        {
            Id = id,
            OwnerId = _ownerId,
            Category = _category,
            ObjectKey = _objectKey,
            Name = _name,
            DeclaredMediaType = _mediaType,
            Size = _size,
            Checksum = _checksum,
            State = StoredFileState.Available,
            RegisteredAt = _registeredAt,
            AvailableAt = availableAt,
            InspectionAttempts = 0,
            NextInspectionAt = null,
            Tags = ["Urgent", "urgent ", "home"],
        });

        file.Id.ShouldBe(id);
        file.OwnerId.ShouldBe(_ownerId);
        file.Category.ShouldBe(_category);
        file.ObjectKey.ShouldBe(_objectKey);
        file.Name.ShouldBe(_name);
        file.DeclaredMediaType.ShouldBe(_mediaType);
        file.Size.ShouldBe(_size);
        file.Checksum.ShouldBe(_checksum);
        file.State.ShouldBe(StoredFileState.Available);
        file.RegisteredAt.ShouldBe(_registeredAt);
        file.AvailableAt.ShouldBe(availableAt);

        file.Tags.Select(tag => tag.Value).ShouldBe(["urgent", "home"], ignoreOrder: true);
    }

    [Fact]
    public void Rehydrate_KeepsTheStoredObjectKey() =>
        Rehydrate().ObjectKey.Value.ShouldBe("t0/202608/0123456789abcdef0123456789abcdef");

    [Fact]
    public void Rehydrate_CannotBeAskedForADeletedFile()
    {
        var parameters = typeof(StoredFile)
            .GetMethod(nameof(StoredFile.Rehydrate))!
            .GetParameters()
            .Select(parameter => parameter.Name!)
            .ToList();

        parameters.ShouldNotBeEmpty();
        parameters.Any(name => name.Contains("delet", StringComparison.OrdinalIgnoreCase)).ShouldBeFalse();
        parameters.Any(name => name.Contains("removed", StringComparison.OrdinalIgnoreCase)).ShouldBeFalse();
    }

    [Fact]
    public void Rehydrate_RaisesNoDomainEvent() => Rehydrate().DomainEvents.ShouldBeEmpty();

    [Fact]
    public void ALoadedPendingFile_CanStillBeConfirmed()
    {
        var file = Rehydrate(state: StoredFileState.Pending);

        file.ConfirmDeposit(_size, _checksum);

        file.State.ShouldBe(StoredFileState.Deposited);
    }

    [Fact]
    public void ALoadedDepositedFile_CanStillReachEitherVerdict()
    {
        Rehydrate(state: StoredFileState.Deposited).MakeAvailable(_registeredAt.AddMinutes(1));
        Rehydrate(state: StoredFileState.Deposited).Quarantine(_registeredAt.AddMinutes(1));

        Should.Throw<DomainException>(
            () => Rehydrate(state: StoredFileState.Quarantined).MakeAvailable(_registeredAt.AddMinutes(1)));
    }

    [Fact]
    public void ALoadedPendingFile_CanBeAbandoned() =>
        Rehydrate(state: StoredFileState.Pending, registeredAt: _registeredAt.AddDays(-7))
            .IsAbandoned(_registeredAt, TimeSpan.FromHours(1))
            .ShouldBeTrue();

    #endregion
}

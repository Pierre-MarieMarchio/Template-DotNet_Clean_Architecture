using AppTemplate.Domain.Core.Common.Exceptions;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.Entities;
using AppTemplate.Domain.Features.Files.Model.Events;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.UnitTests.Features.Files.Model.Entities;

public sealed class StoredFileTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly UserId _ownerId = UserId.Create(Guid.CreateVersion7());
    private static readonly FileCategory _category = FileCategory.Create("document");
    private static readonly StoredFileName _name = StoredFileName.Create("quarterly-report.pdf");
    private static readonly DeclaredMediaType _mediaType = DeclaredMediaType.Create("application/pdf");
    private static readonly FileSize _size = FileSize.Create(4096);

    private static readonly Sha256Checksum _checksum =
        Sha256Checksum.Create("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");

    private static readonly Sha256Checksum _otherChecksum =
        Sha256Checksum.Create("2c26b46b68ffc68ff99b453c1d30413413422d706483bfa0f98a5e886266e7ae");

    private static StoredFile APendingFile(DateTimeOffset? registeredAt = null) =>
        StoredFile.Register(_ownerId, _category, _name, _mediaType, _size, _checksum, registeredAt ?? _now);

    private static StoredFile ADepositedFile()
    {
        var file = APendingFile();
        file.ConfirmDeposit(_size, _checksum);

        return file;
    }

    private static StoredFile AnAvailableFile()
    {
        var file = ADepositedFile();
        file.MakeAvailable(_now.AddMinutes(1));
        file.ClearDomainEvents();

        return file;
    }

    private static StoredFile AQuarantinedFile()
    {
        var file = ADepositedFile();
        file.Quarantine(_now.AddMinutes(1));
        file.ClearDomainEvents();

        return file;
    }

    [Fact]
    public void Register_Rejects_AnAbsentOwner()
    {
        var exception = Should.Throw<ArgumentNullException>(
            () => StoredFile.Register(null!, _category, _name, _mediaType, _size, _checksum, _now));

        exception.ParamName.ShouldBe("ownerId");
    }

    [Fact]
    public void Register_Rejects_TheDefaultInstant()
    {
        var exception = Should.Throw<DomainException>(
            () => StoredFile.Register(_ownerId, _category, _name, _mediaType, _size, _checksum, default));

        exception.Message.ShouldContain("registered");
    }

    [Fact]
    public void Register_Rejects_ANullValueObject()
    {
        Should.Throw<ArgumentNullException>(
            () => StoredFile.Register(_ownerId, null!, _name, _mediaType, _size, _checksum, _now));
        Should.Throw<ArgumentNullException>(
            () => StoredFile.Register(_ownerId, _category, null!, _mediaType, _size, _checksum, _now));
        Should.Throw<ArgumentNullException>(
            () => StoredFile.Register(_ownerId, _category, _name, null!, _size, _checksum, _now));
        Should.Throw<ArgumentNullException>(
            () => StoredFile.Register(_ownerId, _category, _name, _mediaType, null!, _checksum, _now));
        Should.Throw<ArgumentNullException>(
            () => StoredFile.Register(_ownerId, _category, _name, _mediaType, _size, null!, _now));
    }

    [Fact]
    public void Register_StartsTheFilePendingWithEverythingItWasGiven()
    {
        var file = APendingFile();

        file.Id.ShouldNotBe(Guid.Empty);
        file.OwnerId.ShouldBe(_ownerId);
        file.Category.ShouldBe(_category);
        file.Name.ShouldBe(_name);
        file.DeclaredMediaType.ShouldBe(_mediaType);
        file.Size.ShouldBe(_size);
        file.Checksum.ShouldBe(_checksum);
        file.State.ShouldBe(StoredFileState.Pending);
        file.RegisteredAt.ShouldBe(_now);
        file.AvailableAt.ShouldBeNull();
    }

    [Fact]
    public void Register_GivesEveryFileADistinctId() => APendingFile().Id.ShouldNotBe(APendingFile().Id);

    [Fact]
    public void Register_GivesEveryFileADistinctObjectKey() =>
        APendingFile().ObjectKey.ShouldNotBe(APendingFile().ObjectKey);

    [Fact]
    public void Register_DoesNotDeriveTheObjectKeyFromTheId()
    {
        var file = APendingFile();

        file.ObjectKey.Value.ShouldNotContain(file.Id.ToString());
        file.ObjectKey.Value.ShouldNotContain(file.Id.ToString("N"));
    }

    [Theory]
    [InlineData(2026, 8, 9)]
    [InlineData(2026, 12, 31)]
    [InlineData(2027, 1, 1)]
    public void Register_FilesTheBytesInTheSliceOfTheRegistrationInstant(int year, int month, int day)
    {
        var registeredAt = new DateTimeOffset(year, month, day, 23, 45, 0, TimeSpan.FromHours(5));

        var file = APendingFile(registeredAt);

        file.ObjectKey.Value.Split('/')[1].ShouldBe(ObjectKey.TimeSegmentFor(file.RegisteredAt));
    }

    [Fact]
    public void Register_RaisesNoDomainEvent() => APendingFile().DomainEvents.ShouldBeEmpty();

    [Fact]
    public void ConfirmDeposit_MovesAPendingFileToDeposited()
    {
        var file = APendingFile();

        file.ConfirmDeposit(_size, _checksum);

        file.State.ShouldBe(StoredFileState.Deposited);
    }

    [Fact]
    public void ConfirmDeposit_DoesNotMakeTheFileAvailable()
    {
        var file = ADepositedFile();

        file.State.ShouldNotBe(StoredFileState.Available);
        file.AvailableAt.ShouldBeNull();
    }

    [Fact]
    public void ConfirmDeposit_RaisesNoDomainEvent() => ADepositedFile().DomainEvents.ShouldBeEmpty();

    [Fact]
    public void ConfirmDeposit_Rejects_ASizeThatDoesNotMatchWhatWasDeclared()
    {
        var file = APendingFile();

        var exception = Should.Throw<DomainException>(
            () => file.ConfirmDeposit(FileSize.Create(_size.Bytes + 1), _checksum));

        exception.Message.ShouldContain("size");
        file.State.ShouldBe(StoredFileState.Pending);
        file.AvailableAt.ShouldBeNull();
    }

    [Fact]
    public void ConfirmDeposit_Rejects_AChecksumThatDoesNotMatchWhatWasDeclared()
    {
        var file = APendingFile();

        var exception = Should.Throw<DomainException>(() => file.ConfirmDeposit(_size, _otherChecksum));

        exception.Message.ShouldContain("checksum");
        file.State.ShouldBe(StoredFileState.Pending);
    }

    [Fact]
    public void ConfirmDeposit_Accepts_AChecksumReportedInADifferentCase()
    {
        var file = APendingFile();

        file.ConfirmDeposit(_size, Sha256Checksum.Create(_checksum.Value.ToUpperInvariant()));

        file.State.ShouldBe(StoredFileState.Deposited);
    }

    [Theory]
    [InlineData(StoredFileState.Deposited)]
    [InlineData(StoredFileState.Available)]
    [InlineData(StoredFileState.Quarantined)]
    public void ConfirmDeposit_Rejects_AFileThatIsNotPending(StoredFileState state)
    {
        var file = FileIn(state);

        var exception = Should.Throw<DomainException>(() => file.ConfirmDeposit(_size, _checksum));

        exception.Message.ShouldContain("pending");
        file.State.ShouldBe(state);
    }

    [Fact]
    public void ConfirmDeposit_Rejects_ANullObservedValue()
    {
        var file = APendingFile();

        Should.Throw<ArgumentNullException>(() => file.ConfirmDeposit(null!, _checksum));
        Should.Throw<ArgumentNullException>(() => file.ConfirmDeposit(_size, null!));
    }

    [Fact]
    public void MakeAvailable_MovesADepositedFileToAvailable()
    {
        var file = ADepositedFile();
        var releasedAt = _now.AddMinutes(5);

        file.MakeAvailable(releasedAt);

        file.State.ShouldBe(StoredFileState.Available);
        file.AvailableAt.ShouldBe(releasedAt);
    }

    [Fact]
    public void MakeAvailable_RaisesAMadeAvailableEvent_WithTheFilesOwnValues()
    {
        var file = ADepositedFile();
        var releasedAt = _now.AddMinutes(5);

        file.MakeAvailable(releasedAt);

        var raised = file.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<StoredFileMadeAvailableDomainEvent>();
        raised.StoredFileId.ShouldBe(file.Id);
        raised.OwnerId.ShouldBe(_ownerId);
        raised.ObjectKey.ShouldBe(file.ObjectKey);
        raised.DeclaredMediaType.ShouldBe(_mediaType);
        raised.OccurredOn.ShouldBe(releasedAt);
    }

    [Fact]
    public void MakeAvailable_Rejects_AQuarantinedFile()
    {
        var file = AQuarantinedFile();

        var exception = Should.Throw<DomainException>(() => file.MakeAvailable(_now.AddHours(1)));

        exception.Message.ShouldContain("confirmed deposit");
        file.State.ShouldBe(StoredFileState.Quarantined);
        file.AvailableAt.ShouldBeNull();
        file.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void MakeAvailable_Rejects_APendingFile()
    {
        var file = APendingFile();

        Should.Throw<DomainException>(() => file.MakeAvailable(_now.AddMinutes(1)));

        file.State.ShouldBe(StoredFileState.Pending);
        file.AvailableAt.ShouldBeNull();
    }

    [Fact]
    public void MakeAvailable_Rejects_AFileAlreadyAvailable()
    {
        var file = AnAvailableFile();

        Should.Throw<DomainException>(() => file.MakeAvailable(_now.AddHours(1)));

        file.AvailableAt.ShouldBe(_now.AddMinutes(1));
        file.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Quarantine_MovesADepositedFileToQuarantined()
    {
        var file = ADepositedFile();

        file.Quarantine(_now.AddMinutes(5));

        file.State.ShouldBe(StoredFileState.Quarantined);
    }

    [Fact]
    public void Quarantine_LeavesTheAvailabilityInstantUnset() =>
        AQuarantinedFile().AvailableAt.ShouldBeNull();

    [Fact]
    public void Quarantine_RaisesAQuarantinedEvent_CarryingTheObjectKey()
    {
        var file = ADepositedFile();
        var refusedAt = _now.AddMinutes(5);

        file.Quarantine(refusedAt);

        var raised = file.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<StoredFileQuarantinedDomainEvent>();
        raised.StoredFileId.ShouldBe(file.Id);
        raised.OwnerId.ShouldBe(_ownerId);
        raised.ObjectKey.ShouldBe(file.ObjectKey);
        raised.OccurredOn.ShouldBe(refusedAt);
    }

    [Fact]
    public void Quarantine_LeavesTheObjectKeyNamingTheSameBytes()
    {
        var file = ADepositedFile();
        var keyBefore = file.ObjectKey;

        file.Quarantine(_now.AddMinutes(5));

        file.ObjectKey.ShouldBe(keyBefore);
    }

    [Fact]
    public void Quarantine_Rejects_APendingFile()
    {
        var file = APendingFile();

        Should.Throw<DomainException>(() => file.Quarantine(_now.AddMinutes(1)));

        file.State.ShouldBe(StoredFileState.Pending);
        file.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Quarantine_Rejects_AnAvailableFile()
    {
        var file = AnAvailableFile();

        Should.Throw<DomainException>(() => file.Quarantine(_now.AddHours(1)));

        file.State.ShouldBe(StoredFileState.Available);
        file.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Quarantine_Rejects_AFileAlreadyQuarantined()
    {
        var file = AQuarantinedFile();

        Should.Throw<DomainException>(() => file.Quarantine(_now.AddHours(1)));

        file.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void DeferInspection_CountsTheAttemptAndWaitsBeforeTheNext()
    {
        var file = ADepositedFile();

        file.DeferInspection(_now);

        file.InspectionAttempts.ShouldBe(1);
        file.NextInspectionAt.ShouldBe(_now + TimeSpan.FromMinutes(1));
        file.State.ShouldBe(StoredFileState.Deposited);
        file.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void DeferInspection_LengthensTheWaitWithEachAttempt()
    {
        var file = ADepositedFile();

        file.DeferInspection(_now);
        file.DeferInspection(_now.AddMinutes(1));

        file.InspectionAttempts.ShouldBe(2);
        file.NextInspectionAt.ShouldBe(_now.AddMinutes(1) + TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void DeferInspection_NeverQuarantines()
    {
        var file = ADepositedFile();

        for (int attempt = 0; attempt < 100; attempt++)
        {
            file.DeferInspection(_now.AddHours(attempt));
        }

        file.State.ShouldBe(StoredFileState.Deposited);
        file.NextInspectionAt.ShouldBe(_now.AddHours(99) + TimeSpan.FromHours(1));
    }

    [Theory]
    [InlineData(StoredFileState.Pending)]
    [InlineData(StoredFileState.Available)]
    [InlineData(StoredFileState.Quarantined)]
    public void DeferInspection_Rejects_AFileThatIsNotDeposited(StoredFileState state)
    {
        var file = FileIn(state);

        Should.Throw<DomainException>(() => file.DeferInspection(_now));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(6, 32)]
    [InlineData(7, 60)]
    [InlineData(8, 60)]
    [InlineData(1_000_000, 60)]
    public void InspectionRetryDelayAfter_DoublesFromAMinuteUpToAnHour(int attempts, int expectedMinutes) =>
        StoredFile.InspectionRetryDelayAfter(attempts).ShouldBe(TimeSpan.FromMinutes(expectedMinutes));

    [Fact]
    public void InspectionRetryDelayAfter_Rejects_NoAttemptAtAll() =>
        Should.Throw<ArgumentOutOfRangeException>(() => StoredFile.InspectionRetryDelayAfter(0));

    [Fact]
    public void AVerdictAfterDeferrals_KeepsTheirRecord()
    {
        var file = ADepositedFile();
        file.DeferInspection(_now);

        file.MakeAvailable(_now.AddMinutes(2));

        file.InspectionAttempts.ShouldBe(1);
        file.NextInspectionAt.ShouldBe(_now.AddMinutes(1));
    }

    [Fact]
    public void Delete_RaisesADeletedEvent_CarryingTheObjectKey()
    {
        var file = AnAvailableFile();
        var deletedAt = _now.AddHours(1);

        file.Delete(deletedAt);

        var raised = file.DomainEvents.ShouldHaveSingleItem()
            .ShouldBeOfType<StoredFileDeletedDomainEvent>();
        raised.StoredFileId.ShouldBe(file.Id);
        raised.OwnerId.ShouldBe(_ownerId);
        raised.ObjectKey.ShouldBe(file.ObjectKey);
        raised.OccurredOn.ShouldBe(deletedAt);
    }

    [Fact]
    public void Delete_ChangesNothingAboutTheFile()
    {
        var file = AnAvailableFile();
        var stateBefore = file.State;
        var availableAtBefore = file.AvailableAt;
        var keyBefore = file.ObjectKey;

        file.Delete(_now.AddHours(1));

        file.State.ShouldBe(stateBefore);
        file.AvailableAt.ShouldBe(availableAtBefore);
        file.ObjectKey.ShouldBe(keyBefore);
        file.RegisteredAt.ShouldBe(_now);
    }

    [Theory]
    [InlineData(StoredFileState.Pending)]
    [InlineData(StoredFileState.Deposited)]
    [InlineData(StoredFileState.Quarantined)]
    public void Delete_IsAllowedInEveryState(StoredFileState state)
    {
        var file = FileIn(state);
        file.ClearDomainEvents();

        file.Delete(_now.AddHours(1));

        file.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<StoredFileDeletedDomainEvent>();
        file.State.ShouldBe(state);
    }

    [Fact]
    public void Delete_Rejects_ASecondCallInTheSameUnitOfWork()
    {
        var file = AnAvailableFile();
        file.Delete(_now.AddHours(1));

        var exception = Should.Throw<DomainException>(() => file.Delete(_now.AddHours(2)));

        exception.Message.ShouldContain("already been deleted");
        file.DomainEvents.Count.ShouldBe(1);
    }

    [Fact]
    public void Delete_GuardsOnlyTheCurrentUnitOfWork()
    {
        var file = AnAvailableFile();
        file.Delete(_now.AddHours(1));
        file.ClearDomainEvents();

        Should.NotThrow(() => file.Delete(_now.AddHours(2)));
    }

    [Fact]
    public void Delete_DoesNotDisturbAnEventRaisedEarlierInTheSameUnitOfWork()
    {
        var file = ADepositedFile();
        file.MakeAvailable(_now.AddMinutes(1));

        file.Delete(_now.AddMinutes(2));

        file.DomainEvents.Count.ShouldBe(2);
        file.DomainEvents.OfType<StoredFileMadeAvailableDomainEvent>().ShouldHaveSingleItem();
        file.DomainEvents.OfType<StoredFileDeletedDomainEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void IsAbandoned_IsFalse_WhileTheWindowHasNotElapsed()
    {
        var file = APendingFile();

        file.IsAbandoned(_now.AddMinutes(59), TimeSpan.FromHours(1)).ShouldBeFalse();
    }

    [Fact]
    public void IsAbandoned_IsTrue_OnceTheWindowHasElapsed()
    {
        var file = APendingFile();

        file.IsAbandoned(_now.AddHours(2), TimeSpan.FromHours(1)).ShouldBeTrue();
    }

    [Fact]
    public void IsAbandoned_IsTrue_ExactlyAtTheWindow()
    {
        var file = APendingFile();

        file.IsAbandoned(_now.AddHours(1), TimeSpan.FromHours(1)).ShouldBeTrue();
    }

    [Theory]
    [InlineData(StoredFileState.Deposited)]
    [InlineData(StoredFileState.Available)]
    [InlineData(StoredFileState.Quarantined)]
    public void IsAbandoned_IsFalse_ForAnyFileThatWasDeposited(StoredFileState state) =>
        FileIn(state).IsAbandoned(_now.AddYears(1), TimeSpan.FromHours(1)).ShouldBeFalse();

    [Fact]
    public void TheLegalPath_RunsFromPendingToDepositedToAvailableToARemovedRow()
    {
        var file = APendingFile();
        file.State.ShouldBe(StoredFileState.Pending);

        file.ConfirmDeposit(_size, _checksum);
        file.State.ShouldBe(StoredFileState.Deposited);

        file.MakeAvailable(_now.AddMinutes(1));
        file.State.ShouldBe(StoredFileState.Available);

        file.Delete(_now.AddMinutes(2));

        file.RegisteredAt.ShouldBe(_now);
        file.AvailableAt.ShouldBe(_now.AddMinutes(1));
        file.DomainEvents.Count.ShouldBe(2);
    }

    [Fact]
    public void TheRefusedPath_RunsFromPendingToDepositedToQuarantined_AndStops()
    {
        var file = APendingFile();
        file.ConfirmDeposit(_size, _checksum);
        file.Quarantine(_now.AddMinutes(1));

        Should.Throw<DomainException>(() => file.MakeAvailable(_now.AddMinutes(2)));
        Should.Throw<DomainException>(() => file.Quarantine(_now.AddMinutes(2)));
        Should.Throw<DomainException>(() => file.ConfirmDeposit(_size, _checksum));

        file.State.ShouldBe(StoredFileState.Quarantined);
    }

    [Fact]
    public void TheStateMachine_HasNoStateMeaningDeleted()
    {
        string[] states = Enum.GetNames<StoredFileState>();

        states.ShouldBe(["Pending", "Deposited", "Available", "Quarantined"], ignoreOrder: true);
        states.Any(name => name.Contains("delete", StringComparison.OrdinalIgnoreCase)).ShouldBeFalse();
        states.Any(name => name.Contains("removed", StringComparison.OrdinalIgnoreCase)).ShouldBeFalse();
    }

    [Fact]
    public void TheStateMachine_KeepsTheNumbersAlreadyWrittenToRows()
    {
        ((int)StoredFileState.Pending).ShouldBe(0);
        ((int)StoredFileState.Available).ShouldBe(1);
        ((int)StoredFileState.Deposited).ShouldBe(2);
        ((int)StoredFileState.Quarantined).ShouldBe(3);
    }

    private static StoredFile FileIn(StoredFileState state) => state switch
    {
        StoredFileState.Pending => APendingFile(),
        StoredFileState.Deposited => ADepositedFile(),
        StoredFileState.Available => AnAvailableFile(),
        StoredFileState.Quarantined => AQuarantinedFile(),
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown stored file state."),
    };
}

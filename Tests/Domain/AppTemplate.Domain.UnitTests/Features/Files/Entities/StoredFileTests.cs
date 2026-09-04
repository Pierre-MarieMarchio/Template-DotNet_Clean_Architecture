using AppTemplate.Domain.Common.Exceptions;
using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Domain.Features.Files.Events;
using AppTemplate.Domain.Features.Files.ValueObjects;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.UnitTests.Features.Files.Entities;

public sealed class StoredFileTests
{
    private static readonly DateTimeOffset _now = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid _ownerId = Guid.CreateVersion7();
    private static readonly StoredFileName _name = StoredFileName.Create("quarterly-report.pdf");
    private static readonly DeclaredMediaType _mediaType = DeclaredMediaType.Create("application/pdf");
    private static readonly FileSize _size = FileSize.Create(4096);

    private static readonly Sha256Checksum _checksum =
        Sha256Checksum.Create("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");

    private static readonly Sha256Checksum _otherChecksum =
        Sha256Checksum.Create("2c26b46b68ffc68ff99b453c1d30413413422d706483bfa0f98a5e886266e7ae");

    private static StoredFile APendingFile(DateTimeOffset? registeredAt = null) =>
        StoredFile.Register(_ownerId, _name, _mediaType, _size, _checksum, registeredAt ?? _now);

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

    #region Registering

    [Fact]
    public void Register_Rejects_AnEmptyOwnerId()
    {
        var exception = Should.Throw<DomainException>(
            () => StoredFile.Register(Guid.Empty, _name, _mediaType, _size, _checksum, _now));

        exception.Message.ShouldContain("owner");
    }

    /// <summary>
    /// The default instant is what an uninitialised caller passes. Accepting it would register a file
    /// as of 0001-01-01, which the abandonment sweep would find immediately and remove before its
    /// owner could deposit anything — and would file its bytes under a slice a thousand years away
    /// from every other.
    /// </summary>
    [Fact]
    public void Register_Rejects_TheDefaultInstant()
    {
        var exception = Should.Throw<DomainException>(
            () => StoredFile.Register(_ownerId, _name, _mediaType, _size, _checksum, default));

        exception.Message.ShouldContain("registered");
    }

    [Fact]
    public void Register_Rejects_ANullValueObject()
    {
        Should.Throw<ArgumentNullException>(
            () => StoredFile.Register(_ownerId, null!, _mediaType, _size, _checksum, _now));
        Should.Throw<ArgumentNullException>(
            () => StoredFile.Register(_ownerId, _name, null!, _size, _checksum, _now));
        Should.Throw<ArgumentNullException>(
            () => StoredFile.Register(_ownerId, _name, _mediaType, null!, _checksum, _now));
        Should.Throw<ArgumentNullException>(
            () => StoredFile.Register(_ownerId, _name, _mediaType, _size, null!, _now));
    }

    [Fact]
    public void Register_StartsTheFilePendingWithEverythingItWasGiven()
    {
        var file = APendingFile();

        file.Id.ShouldNotBe(Guid.Empty);
        file.OwnerId.ShouldBe(_ownerId);
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

    /// <summary>
    /// The key is minted per file, not per name, per owner or per anything else a second file could
    /// share. Two registrations of the same file by the same owner must not land on one object, or
    /// the second deposit would overwrite the first.
    /// </summary>
    [Fact]
    public void Register_GivesEveryFileADistinctObjectKey() =>
        APendingFile().ObjectKey.ShouldNotBe(APendingFile().ObjectKey);

    /// <summary>
    /// The decision this feature is built around, asserted rather than merely documented: the key is
    /// generated, and no part of it is a function of the id. A future refactor that "simplified" the
    /// key to something derived would pass every other test in this file.
    /// </summary>
    [Fact]
    public void Register_DoesNotDeriveTheObjectKeyFromTheId()
    {
        var file = APendingFile();

        file.ObjectKey.Value.ShouldNotContain(file.Id.ToString());
        file.ObjectKey.Value.ShouldNotContain(file.Id.ToString("N"));
    }

    /// <summary>
    /// The invariant the orphan sweep is built on. Reclaiming bytes is done by difference — list one
    /// slice of the store, subtract the keys of the rows registered in that slice, delete what is
    /// left — and it is only correct because an object under a slice can only have been minted by a
    /// row whose registration instant falls in it. If the key's slice and <c>RegisteredAt</c> could
    /// disagree, the sweep would find a live file's bytes unreferenced and delete them.
    /// </summary>
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

    /// <summary>
    /// Nothing has happened yet that anything else could act on: there is a name and a promise, and
    /// no content behind either. An event here would tell a consumer to go and read bytes that are
    /// not there.
    /// </summary>
    [Fact]
    public void Register_RaisesNoDomainEvent() => APendingFile().DomainEvents.ShouldBeEmpty();

    #endregion

    #region Confirming a deposit

    [Fact]
    public void ConfirmDeposit_MovesAPendingFileToDeposited()
    {
        var file = APendingFile();

        file.ConfirmDeposit(_size, _checksum);

        file.State.ShouldBe(StoredFileState.Deposited);
    }

    /// <summary>
    /// The step that used to make a file readable and no longer does. Confirming says the bytes
    /// arrived; nothing has read them, so nothing may be served yet, and the instant that means
    /// "servable since" must stay unset.
    /// </summary>
    [Fact]
    public void ConfirmDeposit_DoesNotMakeTheFileAvailable()
    {
        var file = ADepositedFile();

        file.State.ShouldNotBe(StoredFileState.Available);
        file.AvailableAt.ShouldBeNull();
    }

    /// <summary>
    /// An event here would be an invitation to inspect the content from a consumer — and consumers
    /// are dispatched in-process after the commit, which is inside the very request the inspection is
    /// arranged to stay out of.
    /// </summary>
    [Fact]
    public void ConfirmDeposit_RaisesNoDomainEvent() => ADepositedFile().DomainEvents.ShouldBeEmpty();

    /// <summary>
    /// The observed values come from the object store, and a mismatch means the bytes that were
    /// deposited are not the ones that were promised. Leaving the file pending is the safe direction:
    /// nothing becomes servable, and the abandonment sweep removes the registration on its own
    /// schedule with nobody having to handle the failure.
    /// </summary>
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

    /// <summary>
    /// The size matches, so nothing but the checksum can account for the refusal — which is what
    /// proves the checksum is actually compared rather than merely stored. A file of the right length
    /// and the wrong content is exactly what a swapped upload looks like.
    /// </summary>
    [Fact]
    public void ConfirmDeposit_Rejects_AChecksumThatDoesNotMatchWhatWasDeclared()
    {
        var file = APendingFile();

        var exception = Should.Throw<DomainException>(() => file.ConfirmDeposit(_size, _otherChecksum));

        exception.Message.ShouldContain("checksum");
        file.State.ShouldBe(StoredFileState.Pending);
    }

    /// <summary>
    /// The client may send its digest in either case and the store may report it in the other. The
    /// comparison is between value objects, which normalise, so a confirmation must not turn on how
    /// either side happened to spell it.
    /// </summary>
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

    #endregion

    #region Releasing an inspected file

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

    /// <summary>
    /// <b>The test that buys the security of the whole feature.</b> A file that was refused must not
    /// be releasable by any route — a second pass, a retried message, a caller reaching the aggregate
    /// by a path nobody has written yet. The guard is on the aggregate rather than on the caller
    /// precisely so that none of those has to remember.
    /// </summary>
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

    /// <summary>
    /// A file whose bytes were never confirmed has nothing to have been inspected. Releasing one
    /// would hand out a grant for a key that may hold nothing at all.
    /// </summary>
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

    #endregion

    #region Quarantining a refused file

    [Fact]
    public void Quarantine_MovesADepositedFileToQuarantined()
    {
        var file = ADepositedFile();

        file.Quarantine(_now.AddMinutes(5));

        file.State.ShouldBe(StoredFileState.Quarantined);
    }

    /// <summary>
    /// The instant means "servable since", so a file that never became servable must not carry one.
    /// It is also the equivalence <c>Rehydrate</c> refuses to load a row against, so writing it here
    /// would produce an aggregate that could not be saved and read back.
    /// </summary>
    [Fact]
    public void Quarantine_LeavesTheAvailabilityInstantUnset() =>
        AQuarantinedFile().AvailableAt.ShouldBeNull();

    /// <summary>
    /// The key travels with the event because the one thing a consumer could reasonably want to do is
    /// reach the bytes. No reason travels with it, deliberately: a refusal is published as a fact,
    /// and which detector fired stays in the operator's log.
    /// </summary>
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

    /// <summary>
    /// Quarantining removes nothing from the object store, and this is where that decision is
    /// asserted rather than only described: the row goes on naming the same key, which is what leaves
    /// the bytes reachable to an investigation and what keeps deletion — the guaranteed path — the
    /// only thing that reclaims them.
    /// </summary>
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

    /// <summary>
    /// A file already served cannot be un-served by changing a column: whoever holds a download grant
    /// for it keeps it, and the bytes are already wherever they were fetched to. So the aggregate
    /// refuses rather than pretending the refusal means anything.
    /// </summary>
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

    #endregion

    #region Deleting

    /// <summary>
    /// The key travels with the event because by the time a consumer runs — after the commit — the
    /// row it would have read no longer exists.
    /// </summary>
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

    /// <summary>
    /// The property that lets the repository keep its "a row is either there or it is not" rule: the
    /// aggregate records nothing about having been deleted, so no query has to remember to filter it
    /// out. Deletion is the removed row; this method only asks for the bytes back sooner.
    /// </summary>
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

    /// <summary>
    /// A client may give up before depositing anything, and a refused file is one its owner will want
    /// rid of. Nothing about deletion depends on the state, which is what removing the deleted state
    /// bought — and it is also the only thing that ever reclaims a quarantined file's bytes.
    /// </summary>
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

    /// <summary>
    /// Two announcements would ask for the same bytes to be reclaimed twice. Harmless in itself —
    /// deleting an absent object is a no-op — but it is a caller doing something it did not mean to,
    /// and the aggregate can see it, so it says so.
    /// </summary>
    [Fact]
    public void Delete_Rejects_ASecondCallInTheSameUnitOfWork()
    {
        var file = AnAvailableFile();
        file.Delete(_now.AddHours(1));

        var exception = Should.Throw<DomainException>(() => file.Delete(_now.AddHours(2)));

        exception.Message.ShouldContain("already been deleted");
        file.DomainEvents.Count.ShouldBe(1);
    }

    /// <summary>
    /// The honest limit of that guard, asserted so nobody mistakes it for more than it is. The check
    /// reads the pending event list, so clearing it — which is what dispatch does after a commit —
    /// resets it. That is not a hole: after the commit the row is gone, so there is no aggregate to
    /// call this on again. Anything that kept one around and re-deleted it would be a bug in the
    /// caller, and this is not the thing that would catch it.
    /// </summary>
    [Fact]
    public void Delete_GuardsOnlyTheCurrentUnitOfWork()
    {
        var file = AnAvailableFile();
        file.Delete(_now.AddHours(1));
        file.ClearDomainEvents();

        Should.NotThrow(() => file.Delete(_now.AddHours(2)));
    }

    /// <summary>
    /// Releasing and deleting in one unit of work is legitimate — an inspection pass that clears a
    /// file its owner has meanwhile asked to remove — and both events must go out: one asks for a
    /// derivative, the other takes the bytes away, and the consumers sort themselves out because each
    /// re-derives its own precondition.
    /// </summary>
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

    #endregion

    #region Abandonment

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

    /// <summary>Pins the boundary, which the implementation includes.</summary>
    [Fact]
    public void IsAbandoned_IsTrue_ExactlyAtTheWindow()
    {
        var file = APendingFile();

        file.IsAbandoned(_now.AddHours(1), TimeSpan.FromHours(1)).ShouldBeTrue();
    }

    /// <summary>
    /// The window has long elapsed and each file is otherwise a perfect candidate — the only thing
    /// wrong with them is their state.
    /// <para>
    /// <b>The deposited case is the one that would cost real data.</b> A file waiting for a verdict
    /// has had its bytes uploaded and checked; if the abandonment sweep could reach it, a scanner
    /// down for longer than the abandonment delay would turn its own outage into deleted uploads,
    /// silently and for everybody at once.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(StoredFileState.Deposited)]
    [InlineData(StoredFileState.Available)]
    [InlineData(StoredFileState.Quarantined)]
    public void IsAbandoned_IsFalse_ForAnyFileThatWasDeposited(StoredFileState state) =>
        FileIn(state).IsAbandoned(_now.AddYears(1), TimeSpan.FromHours(1)).ShouldBeFalse();

    #endregion

    #region The state machine as a whole

    /// <summary>
    /// The whole legal path in one test, because the interesting property is that each step leaves
    /// the next one's precondition satisfied — something no single-step test can say.
    /// </summary>
    [Fact]
    public void TheLegalPath_RunsFromPendingToDepositedToAvailableToARemovedRow()
    {
        var file = APendingFile();
        file.State.ShouldBe(StoredFileState.Pending);

        file.ConfirmDeposit(_size, _checksum);
        file.State.ShouldBe(StoredFileState.Deposited);

        file.MakeAvailable(_now.AddMinutes(1));
        file.State.ShouldBe(StoredFileState.Available);

        // The last step is IStoredFileRepository.Remove, which this layer cannot perform. All the
        // aggregate contributes is the announcement that the bytes may go.
        file.Delete(_now.AddMinutes(2));

        file.RegisteredAt.ShouldBe(_now);
        file.AvailableAt.ShouldBe(_now.AddMinutes(1));
        file.DomainEvents.Count.ShouldBe(2);
    }

    /// <summary>
    /// The branch off it, ending where nothing continues. A quarantined file has no transition left,
    /// which is what the three refusals above assert one at a time and this asserts as a property of
    /// the state.
    /// </summary>
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

    /// <summary>
    /// A tripwire on the decision itself, and the one test that would catch the design being undone.
    /// A deleted file is a removed row; a state meaning "gone" would put a predicate in every query,
    /// where the one that forgets it serves a file that was meant to be unreachable — and it would
    /// make a rehydrated aggregate able to be in a state that, by construction, no stored row can be
    /// in.
    /// <para>
    /// <c>Quarantined</c> is not that state and the difference is not a matter of spelling: it costs
    /// no predicate anywhere, because the one thing that hands out bytes asks for <c>Available</c> by
    /// name.
    /// </para>
    /// </summary>
    [Fact]
    public void TheStateMachine_HasNoStateMeaningDeleted()
    {
        var states = Enum.GetNames<StoredFileState>();

        states.ShouldBe(["Pending", "Deposited", "Available", "Quarantined"], ignoreOrder: true);
        states.Any(name => name.Contains("delete", StringComparison.OrdinalIgnoreCase)).ShouldBeFalse();
        states.Any(name => name.Contains("removed", StringComparison.OrdinalIgnoreCase)).ShouldBeFalse();
    }

    /// <summary>
    /// The numbers, pinned. <c>StoredFileState</c> is persisted as its integer value, so renumbering
    /// a member silently reinterprets every row already in the table — a file that was pending would
    /// read back as available, and one that was refused would read back as servable. Nothing else in
    /// the build can see that: the column's type does not change, no migration is generated, and
    /// every test that round-trips an aggregate through the mapper passes, because it writes and
    /// reads the same wrong number.
    /// </summary>
    [Fact]
    public void TheStateMachine_KeepsTheNumbersAlreadyWrittenToRows()
    {
        ((int)StoredFileState.Pending).ShouldBe(0);
        ((int)StoredFileState.Available).ShouldBe(1);
        ((int)StoredFileState.Deposited).ShouldBe(2);
        ((int)StoredFileState.Quarantined).ShouldBe(3);
    }

    #endregion

    private static StoredFile FileIn(StoredFileState state) => state switch
    {
        StoredFileState.Pending => APendingFile(),
        StoredFileState.Deposited => ADepositedFile(),
        StoredFileState.Available => AnAvailableFile(),
        StoredFileState.Quarantined => AQuarantinedFile(),
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown stored file state."),
    };
}

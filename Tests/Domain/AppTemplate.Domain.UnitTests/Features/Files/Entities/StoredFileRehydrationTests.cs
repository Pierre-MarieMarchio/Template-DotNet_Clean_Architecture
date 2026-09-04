using AppTemplate.Domain.Common.Exceptions;
using AppTemplate.Domain.Features.Files.Entities;
using AppTemplate.Domain.Features.Files.ValueObjects;
using Shouldly;
using Xunit;

namespace AppTemplate.Domain.UnitTests.Features.Files.Entities;

/// <summary>
/// The load path, and the rows it has to refuse. Everything refused here describes a row that no
/// sequence of operations on the aggregate could have written: the state and the confirmation
/// instant are two records of the same fact, and where they disagree, loading the row would put the
/// contradiction inside an aggregate — where it surfaces as a file that can never be confirmed, or
/// as one served without its bytes ever having been checked, far from the row that caused it.
/// </summary>
public sealed class StoredFileRehydrationTests
{
    private static readonly DateTimeOffset _registeredAt = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid _ownerId = Guid.CreateVersion7();

    private static readonly ObjectKey _objectKey =
        ObjectKey.Create("t0/202608/0123456789abcdef0123456789abcdef");

    private static readonly StoredFileName _name = StoredFileName.Create("quarterly-report.pdf");
    private static readonly DeclaredMediaType _mediaType = DeclaredMediaType.Create("application/pdf");
    private static readonly FileSize _size = FileSize.Create(4096);

    private static readonly Sha256Checksum _checksum =
        Sha256Checksum.Create("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");

    private static StoredFile Rehydrate(
        Guid? id = null,
        StoredFileState state = StoredFileState.Pending,
        DateTimeOffset? registeredAt = null,
        DateTimeOffset? availableAt = null) =>
        StoredFile.Rehydrate(
            id ?? Guid.CreateVersion7(),
            _ownerId,
            _objectKey,
            _name,
            _mediaType,
            _size,
            _checksum,
            state,
            registeredAt ?? _registeredAt,
            availableAt);

    #region What a stored row must carry

    [Fact]
    public void Rehydrate_Rejects_AnEmptyId()
    {
        var exception = Should.Throw<DomainException>(() => Rehydrate(id: Guid.Empty));

        exception.Message.ShouldContain("id");
    }

    [Fact]
    public void Rehydrate_Rejects_AnEmptyOwnerId() =>
        Should.Throw<DomainException>(
            () => StoredFile.Rehydrate(
                Guid.CreateVersion7(),
                Guid.Empty,
                _objectKey,
                _name,
                _mediaType,
                _size,
                _checksum,
                StoredFileState.Pending,
                _registeredAt,
                null));

    /// <summary>
    /// Without it, a pending row could not answer how long it has been waiting, and the abandonment
    /// sweep would either skip it for ever or remove it immediately depending on which way the
    /// comparison against <c>default</c> fell. It is also the value the orphan sweep derives a
    /// row's storage slice from, so a row without one names bytes the sweep would look for in the
    /// wrong prefix — and therefore never find referenced.
    /// </summary>
    [Fact]
    public void Rehydrate_Rejects_AMissingRegistrationInstant() =>
        Should.Throw<DomainException>(() => Rehydrate(registeredAt: default(DateTimeOffset)));

    [Fact]
    public void Rehydrate_Rejects_ANullValueObject() =>
        Should.Throw<ArgumentNullException>(
            () => StoredFile.Rehydrate(
                Guid.CreateVersion7(),
                _ownerId,
                null!,
                _name,
                _mediaType,
                _size,
                _checksum,
                StoredFileState.Pending,
                _registeredAt,
                null));

    #endregion

    #region Rows that contradict themselves

    [Fact]
    public void Rehydrate_Rejects_AnAvailableFileWithNoAvailabilityInstant()
    {
        var exception = Should.Throw<DomainException>(
            () => Rehydrate(state: StoredFileState.Available, availableAt: null));

        exception.Message.ShouldContain("must record");
    }

    /// <summary>
    /// The converse, over every state that is not available. The instant is written by the one
    /// transition that makes a file servable, so a row in any other state holding one is a row that
    /// was half-written — which is exactly what a mapper that assigns the instant before the state
    /// produces.
    /// <para>
    /// The rule is stated as an equivalence over the whole enum rather than as one clause per state,
    /// and that is what this theory is checking: the two rules it replaced named <c>Available</c> and
    /// <c>Pending</c>, so the day a third member appeared it was exempt from both — a quarantined row
    /// carrying an availability instant would have loaded without complaint.
    /// </para>
    /// </summary>
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

    /// <summary>
    /// Instants are not checked against each other on purpose. Both come from a wall clock an NTP
    /// correction can step backwards, so a row written across such a step is legitimate; refusing it
    /// would make the file unreadable and undeletable over a field nothing makes a decision from.
    /// Asserted so that adding an ordering rule is a deliberate act that fails a test rather than a
    /// tidy-up that quietly bricks rows.
    /// </summary>
    [Fact]
    public void Rehydrate_DoesNotCheckThatTheConfirmationFollowsTheRegistration() =>
        Should.NotThrow(
            () => Rehydrate(
                state: StoredFileState.Available,
                availableAt: _registeredAt.AddMinutes(-5)));

    #endregion

    #region What a loaded file is

    [Fact]
    public void Rehydrate_RestoresTheStoredIdentityAndValues()
    {
        var id = Guid.CreateVersion7();
        var availableAt = _registeredAt.AddMinutes(1);

        var file = StoredFile.Rehydrate(
            id,
            _ownerId,
            _objectKey,
            _name,
            _mediaType,
            _size,
            _checksum,
            StoredFileState.Available,
            _registeredAt,
            availableAt);

        file.Id.ShouldBe(id);
        file.OwnerId.ShouldBe(_ownerId);
        file.ObjectKey.ShouldBe(_objectKey);
        file.Name.ShouldBe(_name);
        file.DeclaredMediaType.ShouldBe(_mediaType);
        file.Size.ShouldBe(_size);
        file.Checksum.ShouldBe(_checksum);
        file.State.ShouldBe(StoredFileState.Available);
        file.RegisteredAt.ShouldBe(_registeredAt);
        file.AvailableAt.ShouldBe(availableAt);
    }

    /// <summary>
    /// The stored key is restored exactly, never re-minted. A load path that generated a fresh key
    /// would point every reloaded file at bytes that do not exist, and would do it silently — the
    /// aggregate would look perfectly healthy, and the orphan sweep would then reclaim the real
    /// bytes, because nothing would reference them any more.
    /// </summary>
    [Fact]
    public void Rehydrate_KeepsTheStoredObjectKey() =>
        Rehydrate().ObjectKey.Value.ShouldBe("t0/202608/0123456789abcdef0123456789abcdef");

    /// <summary>
    /// A loaded row can only ever be a file that exists, because a deleted file is a row that was
    /// removed. There is no state to load a tombstone into and no argument by which to ask for one —
    /// which is what spares every caller from having to check whether the file it just loaded still
    /// counts. Asserted through the signature, since the guarantee is that the parameter does not
    /// exist rather than that some value of it is refused.
    /// </summary>
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

    /// <summary>
    /// A loaded pending file behaves like a registered one: the transitions are a property of the
    /// state, not of how the aggregate came to be in it.
    /// </summary>
    [Fact]
    public void ALoadedPendingFile_CanStillBeConfirmed()
    {
        var file = Rehydrate(state: StoredFileState.Pending);

        file.ConfirmDeposit(_size, _checksum);

        file.State.ShouldBe(StoredFileState.Deposited);
    }

    /// <summary>
    /// The row the inspection pass loads on every tick. It is also the row a host that stopped
    /// mid-pass leaves behind, so a loaded deposited file has to be able to reach both verdicts — and
    /// neither of them may be reachable from a loaded quarantined one, which is the property the
    /// second half asserts.
    /// </summary>
    [Fact]
    public void ALoadedDepositedFile_CanStillReachEitherVerdict()
    {
        Rehydrate(state: StoredFileState.Deposited).MakeAvailable(_registeredAt.AddMinutes(1));
        Rehydrate(state: StoredFileState.Deposited).Quarantine(_registeredAt.AddMinutes(1));

        Should.Throw<DomainException>(
            () => Rehydrate(state: StoredFileState.Quarantined).MakeAvailable(_registeredAt.AddMinutes(1)));
    }

    /// <summary>
    /// A loaded pending file registered long ago is what the abandonment sweep exists to find, and it
    /// is a state <c>Register</c> cannot produce: nothing can be registered in the past.
    /// </summary>
    [Fact]
    public void ALoadedPendingFile_CanBeAbandoned() =>
        Rehydrate(state: StoredFileState.Pending, registeredAt: _registeredAt.AddDays(-7))
            .IsAbandoned(_registeredAt, TimeSpan.FromHours(1))
            .ShouldBeTrue();

    #endregion
}

using AppTemplate.Domain.Common.Abstractions;
using AppTemplate.Domain.Common.Exceptions;
using AppTemplate.Domain.Common.Primitives;
using AppTemplate.Domain.Features.Files.Events;
using AppTemplate.Domain.Features.Files.ValueObjects;

namespace AppTemplate.Domain.Features.Files.Entities;

/// <summary>
/// One file a user put into the system: everything about it except its content. A flat aggregate
/// with no child entities — nothing about a file has to be consistent with anything else in the same
/// transaction.
/// <para>
/// <b>This aggregate never holds a byte.</b> The content lives in an object store and gets there
/// without passing through this application at all: the client registers the file here, receives a
/// signed URL, deposits the bytes directly onto the store, and comes back to confirm. See
/// <c>SECURITY.md</c> for why the API carries no content in either direction.
/// </para>
/// <para>
/// The consequence is the state machine below. Because the row and the bytes are written by two
/// parties at two moments with no transaction spanning them, each state names a moment when the two
/// are known to disagree, and the transition is one party reporting that they agree again.
/// <c>Pending → Deposited → Available</c> is the whole of a successful life, and
/// <c>Deposited → Quarantined</c> is the one branch off it. There is no path back from either
/// terminal state: the bytes under a key never change, so a verdict about them never needs revising.
/// </para>
/// <para>
/// <b>Deleting is removing the row.</b> There is no deleted state and no deletion instant: the
/// file's bytes are reclaimed by a sweep that lists the store and deletes what no live row names, so
/// nothing has to survive the row in order to say which bytes are owed.
/// <see cref="StoredFileState.Quarantined"/> is not an exception — a quarantined file is a live row
/// its owner can see and delete, not a tombstone every query has to filter out.
/// </para>
/// </summary>
public sealed class StoredFile : AggregateRoot<Guid>, IAuditable, IVersioned
{
    /// <summary>
    /// The one place the shared invariants live, so that neither entry point can be given a rule the
    /// other lacks. <see cref="Register"/> and <see cref="Rehydrate"/> add only the checks that are
    /// theirs alone: what a caller may ask for, and what a stored row may claim.
    /// </summary>
    private StoredFile(
        Guid id,
        Guid ownerId,
        ObjectKey objectKey,
        StoredFileName name,
        DeclaredMediaType declaredMediaType,
        FileSize size,
        Sha256Checksum checksum,
        DateTimeOffset registeredAt)
        : base(id)
    {
        if (id == Guid.Empty)
        {
            throw new DomainException("A stored file must have an id.");
        }

        if (ownerId == Guid.Empty)
        {
            throw new DomainException("A stored file must have an owner.");
        }

        ArgumentNullException.ThrowIfNull(objectKey);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(declaredMediaType);
        ArgumentNullException.ThrowIfNull(size);
        ArgumentNullException.ThrowIfNull(checksum);

        if (registeredAt == default)
        {
            throw new DomainException("A stored file must record when it was registered.");
        }

        OwnerId = ownerId;
        ObjectKey = objectKey;
        Name = name;
        DeclaredMediaType = declaredMediaType;
        Size = size;
        Checksum = checksum;
        RegisteredAt = registeredAt;
    }

    /// <summary>
    /// Who the file belongs to. Every authorisation decision about this file reads it, and it is
    /// assigned once: a file does not change hands, so no operation below can move it.
    /// </summary>
    public Guid OwnerId { get; private set; }

    /// <summary>
    /// Where the bytes are. Minted once at registration and never recomputed — see
    /// <see cref="ValueObjects.ObjectKey"/> for the two reasons, which are the load-bearing decision
    /// of this whole feature.
    /// </summary>
    public ObjectKey ObjectKey { get; private set; }

    /// <summary>What the client called the file. A label; it addresses nothing.</summary>
    public StoredFileName Name { get; private set; }

    /// <summary>
    /// What the client said the file is. It keeps the word "declared" for the whole life of the
    /// file, even after the content has been read.
    /// <para>
    /// The reason is that the inspection between <see cref="StoredFileState.Deposited"/> and
    /// <see cref="StoredFileState.Available"/> does not <em>correct</em> this value, it
    /// <em>refuses</em> the file whose content contradicts it. So an available file's declared type
    /// is a claim that survived a check rather than a fact measured from the bytes — the difference
    /// matters to anything downstream that would otherwise treat it as one. <see cref="Size"/> and
    /// <see cref="Checksum"/> are the two values that really are replaced by what the store
    /// measured, which is why only they lose the qualifier.
    /// </para>
    /// </summary>
    public DeclaredMediaType DeclaredMediaType { get; private set; }

    /// <summary>
    /// The client's declaration while the file is <see cref="StoredFileState.Pending"/>, and a fact
    /// the store has agreed with from <see cref="StoredFileState.Deposited"/> onwards:
    /// <see cref="ConfirmDeposit"/> refuses to move the file on unless the two match.
    /// </summary>
    public FileSize Size { get; private set; }

    /// <summary>The same two-lives value as <see cref="Size"/>, over the content rather than its length.</summary>
    public Sha256Checksum Checksum { get; private set; }

    public StoredFileState State { get; private set; } = StoredFileState.Pending;

    /// <summary>
    /// When the file was registered and its key reserved.
    /// <para>
    /// Deliberately not <see cref="CreatedAt"/>, which looks like the same fact. That one is written
    /// by the store's auditing interceptor at flush time, so it is <c>default</c> for the entire
    /// life of the aggregate in memory and an unsaved file could not answer "how long have I been
    /// pending?". More importantly, abandonment is a domain rule — see <see cref="IsAbandoned"/> —
    /// and a domain rule reads a value the domain owns rather than an audit stamp whose meaning
    /// belongs to the persistence layer and could reasonably be redefined there.
    /// </para>
    /// <para>
    /// It is also the value that says which time slice of the object store holds this file's bytes,
    /// via <see cref="ValueObjects.ObjectKey.TimeSegmentFor"/>. The two agree because
    /// <see cref="Register"/> mints the key from this very instant, and nothing can move either
    /// afterwards.
    /// </para>
    /// </summary>
    public DateTimeOffset RegisteredAt { get; private set; }

    /// <summary>
    /// When the file became servable — which is when its content was cleared, not when its bytes
    /// arrived. Null until then, and null for ever for a file that was never deposited against or
    /// whose content was refused. It is the same fact as
    /// <see cref="State"/> being <see cref="StoredFileState.Available"/>, and
    /// <see cref="Rehydrate"/> refuses a row where the two disagree.
    /// </summary>
    public DateTimeOffset? AvailableAt { get; private set; }

    public uint Version { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset? LastModifiedAt { get; private set; }

    public Guid? LastModifiedBy { get; private set; }

    /// <summary>
    /// Reserves a place for a file whose bytes have not been sent yet. Raises no domain event: at
    /// this point nothing has happened that anything else could act on — there is a name and a
    /// promise, and no content behind either.
    /// </summary>
    /// <param name="size">What the client says the file weighs. Checked against the store at
    /// confirmation, so a lie here costs the client its own upload rather than this system anything.</param>
    /// <param name="now">Injected rather than read from a clock, so the aggregate has no ambient
    /// dependency and its behaviour is reproducible in a test. It is both the registration instant
    /// and the instant the key's time slice is minted from, and they have to be the same value.</param>
    public static StoredFile Register(
        Guid ownerId,
        StoredFileName name,
        DeclaredMediaType declaredMediaType,
        FileSize size,
        Sha256Checksum checksum,
        DateTimeOffset now) =>
        new(
            Guid.CreateVersion7(),
            ownerId,
            ObjectKey.New(now),
            name,
            declaredMediaType,
            size,
            checksum,
            now);

    /// <summary>
    /// Rebuilds a file that already exists in a store, from the values that were stored. A row whose
    /// state and instants contradict each other is refused on the way in rather than becoming an
    /// aggregate that cannot honour its own rules.
    /// <para>
    /// There is no deleted file to rebuild, and that is the point of removing the row rather than
    /// flagging it: this method cannot produce a file that is logically gone, so no caller has to
    /// remember to check whether the file it just loaded still counts.
    /// </para>
    /// <para>
    /// One rule is deliberately <em>not</em> checked: that <see cref="AvailableAt"/> is at or after
    /// <see cref="RegisteredAt"/>. It is tempting — a file cannot be confirmed before it was
    /// registered — but both values come from a wall clock that an NTP correction can step
    /// backwards, and a row written across such a step is a legitimate row. Enforcing the order
    /// would make it permanently unloadable, which is worse than the mis-ordered timestamp it would
    /// catch: the file would become unreadable and undeletable over a field nothing makes a decision
    /// from.
    /// </para>
    /// </summary>
    public static StoredFile Rehydrate(
        Guid id,
        Guid ownerId,
        ObjectKey objectKey,
        StoredFileName name,
        DeclaredMediaType declaredMediaType,
        FileSize size,
        Sha256Checksum checksum,
        StoredFileState state,
        DateTimeOffset registeredAt,
        DateTimeOffset? availableAt)
    {
        // The state and the instant are two records of the same fact. Where they disagree the row
        // describes a file that no sequence of operations could have produced, and loading it would
        // put the contradiction inside an aggregate, where it surfaces far from the row that caused
        // it — as a file that can never be confirmed, or as one served without its bytes ever having
        // been checked.
        //
        // Written as an equivalence over the whole enum rather than as one rule per state, because
        // the two rules it replaced were a rule about Available and a rule about Pending, and adding
        // a member to the enum quietly exempted it from both. Only MakeAvailable ever writes the
        // instant, so "has an instant" and "is available" are the same fact and neither may appear
        // without the other.
        if ((state == StoredFileState.Available) != (availableAt is not null))
        {
            throw new DomainException(
                state == StoredFileState.Available
                    ? "An available stored file must record when it was made available."
                    : "Only an available stored file may record when it was made available.");
        }

        return new StoredFile(id, ownerId, objectKey, name, declaredMediaType, size, checksum, registeredAt)
        {
            State = state,
            AvailableAt = availableAt,
        };
    }

    /// <summary>
    /// Records that the bytes are on the store and are the ones that were promised. <b>It does not
    /// make the file servable</b>, and the gap between this and <see cref="MakeAvailable"/> is where
    /// the content is examined.
    /// <para>
    /// The observed values are what the object store reports for the deposited object, not what the
    /// client says a second time — asking the client to confirm its own claim would confirm nothing.
    /// A mismatch leaves the file <see cref="StoredFileState.Pending"/>, which is the safe direction:
    /// nothing is served, and the abandonment sweep removes the registration on its own schedule
    /// without anyone having to handle the failure.
    /// </para>
    /// <para>
    /// It takes no instant and raises no event, and both omissions are deliberate. Nothing decides
    /// anything from how long a file has been waiting for a verdict — the pass that inspects them
    /// takes the oldest first and needs no second timestamp to do it — and an event would invite a
    /// consumer to run the inspection, which is exactly where it must not run: consumers are
    /// dispatched in-process after commit, so a consumer here would put a scan of the whole file
    /// back inside the request that confirmed it.
    /// </para>
    /// </summary>
    /// <exception cref="DomainException">The file is not pending, or the deposit is not what was
    /// declared.</exception>
    public void ConfirmDeposit(FileSize observedSize, Sha256Checksum observedChecksum)
    {
        ArgumentNullException.ThrowIfNull(observedSize);
        ArgumentNullException.ThrowIfNull(observedChecksum);

        if (State != StoredFileState.Pending)
        {
            throw new DomainException("Only a pending stored file can have its deposit confirmed.");
        }

        if (observedSize != Size)
        {
            throw new DomainException("The deposited file does not have the size that was declared.");
        }

        if (observedChecksum != Checksum)
        {
            throw new DomainException("The deposited file does not have the checksum that was declared.");
        }

        State = StoredFileState.Deposited;
    }

    /// <summary>
    /// Releases the file for serving, once its content has been examined and found acceptable. The
    /// only transition that makes a file servable, and the only writer of
    /// <see cref="AvailableAt"/>.
    /// <para>
    /// It refuses anything that is not <see cref="StoredFileState.Deposited"/>, which is the check
    /// that makes quarantine worth having: the state machine, and not the caller's diligence, is
    /// what stops a refused file from being released by a second pass, a retried message or a future
    /// caller reaching the aggregate by a route nobody has written yet.
    /// </para>
    /// </summary>
    /// <exception cref="DomainException">The file has no confirmed deposit, or has already been
    /// released or refused.</exception>
    public void MakeAvailable(DateTimeOffset now)
    {
        if (State != StoredFileState.Deposited)
        {
            throw new DomainException("Only a stored file with a confirmed deposit can be made available.");
        }

        State = StoredFileState.Available;
        AvailableAt = now;

        RaiseDomainEvent(new StoredFileMadeAvailableDomainEvent(Id, OwnerId, ObjectKey, DeclaredMediaType, now));
    }

    /// <summary>
    /// Refuses the file on the evidence of its own content. Terminal, and reachable only from
    /// <see cref="StoredFileState.Deposited"/> — a file whose bytes were never confirmed has nothing
    /// to have a verdict about, and one already served cannot be un-served by changing a column.
    /// <para>
    /// <b>The bytes are left alone.</b> Quarantining removes nothing from the object store, for two
    /// reasons that point the same way. The row still names the key, so the orphan sweep would not
    /// reclaim these bytes anyway and a deletion here would be the <em>only</em> thing that ever
    /// did — precisely the shape this repository refuses, where a message that fails to arrive
    /// becomes a leak instead of a delay. And a refused file is the one file somebody may need to
    /// look at. Getting rid of it is deleting the row, which is what the owner can already do and
    /// what reclaims the bytes on the path that is guaranteed.
    /// </para>
    /// <para>
    /// No reason is recorded — see <see cref="StoredFileState.Quarantined"/>.
    /// </para>
    /// </summary>
    /// <exception cref="DomainException">The file has no confirmed deposit, or has already been
    /// released or refused.</exception>
    public void Quarantine(DateTimeOffset now)
    {
        if (State != StoredFileState.Deposited)
        {
            throw new DomainException("Only a stored file with a confirmed deposit can be quarantined.");
        }

        State = StoredFileState.Quarantined;

        RaiseDomainEvent(new StoredFileQuarantinedDomainEvent(Id, OwnerId, ObjectKey, now));
    }

    /// <summary>
    /// Announces that this file is being deleted, so its bytes can be reclaimed promptly.
    /// <para>
    /// <b>It changes nothing about the file.</b> There is no state to move to and no instant to
    /// record: deleting a file is <c>IStoredFileRepository.Remove</c> plus a commit, and this method
    /// only raises the event that lets the bytes be reclaimed now instead of at the next sweep. It
    /// is called alongside <c>Remove</c>, never instead of it — on its own it deletes nothing, and
    /// the file stays exactly as it was.
    /// </para>
    /// <para>
    /// Refusing a second call in one unit of work is about the event, not about the file: two
    /// announcements would ask for the same bytes to be reclaimed twice. The check reads
    /// <see cref="AggregateRoot{TId}.DomainEvents"/> because that is the only thing here that is
    /// scoped to the unit of work — the aggregate holds no memory of a previous one, which is
    /// correct, since after a commit the row is gone and there is no aggregate to call this on
    /// again.
    /// </para>
    /// </summary>
    /// <exception cref="DomainException">Deletion has already been announced in this unit of work.</exception>
    public void Delete(DateTimeOffset now)
    {
        if (DomainEvents.OfType<StoredFileDeletedDomainEvent>().Any())
        {
            throw new DomainException("This stored file has already been deleted in this unit of work.");
        }

        RaiseDomainEvent(new StoredFileDeletedDomainEvent(Id, OwnerId, ObjectKey, now));
    }

    /// <summary>
    /// Whether this registration has been waiting for a deposit long enough to be given up on. The
    /// sweep that reads it removes the row; the bytes, if the client did deposit some and never
    /// confirmed, are then reclaimed by the orphan sweep like any others.
    /// <para>
    /// The rule lives here and not in the sweep's query for the same reason
    /// <c>Reminder.TryClaim</c> re-checks a due date the query already filtered on: the query is a
    /// coarse filter chosen for an index, and a second host, a maintenance endpoint or a future
    /// caller reaching the aggregate by another route gets the same answer only if the aggregate is
    /// the one giving it.
    /// </para>
    /// <para>
    /// <b>It reads <see cref="StoredFileState.Pending"/> alone, and widening it would delete
    /// people's uploads.</b> A file waiting for a verdict on content that has already arrived is not
    /// a registration nobody used; it is a deposit this system has not finished with. A scanner down
    /// for longer than the abandonment delay would otherwise turn its own outage into data loss.
    /// </para>
    /// </summary>
    public bool IsAbandoned(DateTimeOffset now, TimeSpan abandonedAfter) =>
        State == StoredFileState.Pending && now - RegisteredAt >= abandonedAfter;

    void IAuditable.SetCreated(DateTimeOffset at, Guid? by)
    {
        CreatedAt = at;
        CreatedBy = by;
    }

    void IAuditable.SetLastModified(DateTimeOffset at, Guid? by)
    {
        LastModifiedAt = at;
        LastModifiedBy = by;
    }

    void IVersioned.SetVersion(uint version) => Version = version;
}

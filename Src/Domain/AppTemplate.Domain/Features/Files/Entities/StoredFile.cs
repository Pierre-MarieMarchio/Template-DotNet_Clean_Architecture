using AppTemplate.Domain.Common.Tagging;
using AppTemplate.Domain.Core.Common.Abstractions;
using AppTemplate.Domain.Core.Common.Exceptions;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Events;
using AppTemplate.Domain.Features.Files.ValueObjects;

namespace AppTemplate.Domain.Features.Files.Entities;

/// <summary>
/// One file in the system: its metadata, never its bytes. The row and the bytes are written by two
/// parties with no shared transaction, so the state is <c>Pending → Deposited → Available</c> or
/// <c>Deposited → Quarantined</c>, and neither terminal state can be left. Deleting removes the
/// row; the orphan sweep reclaims the bytes.
/// </summary>
public sealed class StoredFile : AuditableAggregateRoot<Guid>, IOwnedAggregate
{
    /// <summary>How many tags one file carries; the rules are <see cref="TagSet"/>'s.</summary>
    public const int MaxTags = 20;

    private static readonly TimeSpan _firstInspectionRetryDelay = TimeSpan.FromMinutes(1);

    // A deposit is offered again at least hourly.
    private static readonly TimeSpan _longestInspectionRetryDelay = TimeSpan.FromHours(1);

    private readonly TagSet _tags = new(MaxTags, "stored file");

    // The shared invariants live here, so Register and Rehydrate add only the checks that are theirs.
    private StoredFile(
        Guid id,
        UserId ownerId,
        FileCategory category,
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

        ArgumentNullException.ThrowIfNull(ownerId);
        ArgumentNullException.ThrowIfNull(category);
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
        Category = category;
        ObjectKey = objectKey;
        Name = name;
        DeclaredMediaType = declaredMediaType;
        Size = size;
        Checksum = checksum;
        RegisteredAt = registeredAt;
    }

    public IReadOnlyCollection<Tag> Tags => _tags.Tags;

    public UserId OwnerId { get; private set; }

    /// <summary>The configured category; what it allows is read from configuration, never stored.</summary>
    public FileCategory Category { get; private set; }

    /// <summary>Where the bytes are: minted once, never recomputed; see <see cref="ValueObjects.ObjectKey"/>.</summary>
    public ObjectKey ObjectKey { get; private set; }

    public StoredFileName Name { get; private set; }

    /// <summary>
    /// What the client said the file is. Inspection never corrects it, it refuses a file that
    /// contradicts it.
    /// </summary>
    public DeclaredMediaType DeclaredMediaType { get; private set; }

    /// <summary>
    /// The client's declaration while pending, what the store reported from deposited onwards.
    /// </summary>
    public FileSize Size { get; private set; }

    public Sha256Checksum Checksum { get; private set; }

    public StoredFileState State { get; private set; } = StoredFileState.Pending;

    /// <summary>
    /// When the file was registered and its key reserved. Not <see cref="IAuditable.CreatedAt"/>,
    /// which belongs to persistence; abandonment (<see cref="IsAbandoned"/>) is a domain rule.
    /// </summary>
    public DateTimeOffset RegisteredAt { get; private set; }

    /// <summary>When the file was cleared for serving; null until then and for ever for a refused file.</summary>
    public DateTimeOffset? AvailableAt { get; private set; }

    /// <summary>How many inspections of this deposit ended without a verdict.</summary>
    public int InspectionAttempts { get; private set; }

    /// <summary>The earliest instant of the next inspection after one that reached no verdict, or null.</summary>
    public DateTimeOffset? NextInspectionAt { get; private set; }

    /// <summary>
    /// Reserves a place for a file whose bytes have not been sent yet. Raises no event: there is nothing to act on.
    /// </summary>
    /// <param name="now">Injected so the aggregate has no ambient clock; it is both the registration instant and the
    /// one the key's time slice is minted from, and they must be the same value.</param>
    public static StoredFile Register(
        UserId ownerId,
        FileCategory category,
        StoredFileName name,
        DeclaredMediaType declaredMediaType,
        FileSize size,
        Sha256Checksum checksum,
        DateTimeOffset now) =>
        new(
            Guid.CreateVersion7(),
            ownerId,
            category,
            ObjectKey.New(now),
            name,
            declaredMediaType,
            size,
            checksum,
            now);

    /// <summary>
    /// Rebuilds a file from stored values; a row whose state and instants contradict each other is
    /// refused. <see cref="AvailableAt"/> is deliberately not ordered against
    /// <see cref="RegisteredAt"/>: a wall clock an NTP correction can step backwards would make a
    /// legitimate row unloadable over a field nothing decides from.
    /// </summary>
    public static StoredFile Rehydrate(StoredFileSnapshot stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(stored.Tags);

        // An equivalence over the whole enum rather than a rule per state, so a new state is not exempt.
        if ((stored.State == StoredFileState.Available) != (stored.AvailableAt is not null))
        {
            throw new DomainException(
                stored.State == StoredFileState.Available
                    ? "An available stored file must record when it was made available."
                    : "Only an available stored file may record when it was made available.");
        }

        if (stored.InspectionAttempts < 0)
        {
            throw new DomainException("A stored file cannot have a negative number of inspection attempts.");
        }

        if (stored.NextInspectionAt is not null && stored.InspectionAttempts == 0)
        {
            throw new DomainException("A stored file never inspected cannot be waiting for another inspection.");
        }

        var file = new StoredFile(
            stored.Id,
            stored.OwnerId,
            stored.Category,
            stored.ObjectKey,
            stored.Name,
            stored.DeclaredMediaType,
            stored.Size,
            stored.Checksum,
            stored.RegisteredAt)
        {
            State = stored.State,
            AvailableAt = stored.AvailableAt,
            InspectionAttempts = stored.InspectionAttempts,
            NextInspectionAt = stored.NextInspectionAt,
        };

        // Through the set, so too many tags or a duplicate refuse the row.
        file.SetTags(stored.Tags);

        return file;
    }

    /// <summary>
    /// Records that the bytes on the store are the ones promised, from the store's own report; it
    /// does not make the file servable. A mismatch leaves the file pending for the abandonment
    /// sweep. No event is raised, or a consumer would run the inspection inside the confirming
    /// request.
    /// </summary>
    /// <exception cref="DomainException">The file is not pending, or the deposit is not what was declared.</exception>
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
    /// Releases the file for serving once its content was examined and accepted. Only a deposited
    /// file qualifies, so the state machine, not the caller's diligence, stops a refused file
    /// being released by a second pass.
    /// </summary>
    /// <exception cref="DomainException">The file has no confirmed deposit, or was already released or refused.</exception>
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
    /// Refuses the file on the evidence of its content; terminal, from deposited only. The bytes
    /// stay on the store: the row still names the key, so no sweep would reclaim them, and a
    /// refused file is the one somebody may need to look at. No reason is recorded; see
    /// <see cref="StoredFileState.Quarantined"/>.
    /// </summary>
    /// <exception cref="DomainException">The file has no confirmed deposit, or was already released or refused.</exception>
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
    /// Records an inspection that reached no verdict and pushes the next one out: one minute,
    /// doubled per attempt, capped at one hour. It never refuses the file: an outage must not
    /// become a quarantine.
    /// </summary>
    /// <exception cref="DomainException">The file is not deposited.</exception>
    public void DeferInspection(DateTimeOffset now)
    {
        if (State != StoredFileState.Deposited)
        {
            throw new DomainException("Only a deposited stored file can have its inspection deferred.");
        }

        InspectionAttempts++;
        NextInspectionAt = now + InspectionRetryDelayAfter(InspectionAttempts);
    }

    /// <summary>The wait before the next inspection once <paramref name="attempts"/> have reached no verdict.</summary>
    public static TimeSpan InspectionRetryDelayAfter(int attempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);

        var delay = _firstInspectionRetryDelay;

        for (int attempt = 1; attempt < attempts && delay < _longestInspectionRetryDelay; attempt++)
        {
            delay += delay;
        }

        return delay < _longestInspectionRetryDelay ? delay : _longestInspectionRetryDelay;
    }

    /// <summary>
    /// Raises the event that lets the bytes be reclaimed now rather than at the next sweep; it
    /// changes nothing about the file and is called alongside <c>IStoredFileRepository.Remove</c>,
    /// never instead of it.
    /// </summary>
    /// <exception cref="DomainException">Deletion was already announced in this unit of work.</exception>
    public void Delete(DateTimeOffset now)
    {
        // The unit of work's events are the only per-unit-of-work memory the aggregate has.
        if (DomainEvents.OfType<StoredFileDeletedDomainEvent>().Any())
        {
            throw new DomainException("This stored file has already been deleted in this unit of work.");
        }

        RaiseDomainEvent(new StoredFileDeletedDomainEvent(Id, OwnerId, ObjectKey, now));
    }

    /// <summary>
    /// Whether this registration has waited past the deadline. Reads pending alone: a file awaiting
    /// a verdict is a deposit that arrived, and widening this would delete uploads.
    /// </summary>
    public bool IsAbandoned(DateTimeOffset now, TimeSpan abandonedAfter) =>
        State == StoredFileState.Pending && now - RegisteredAt >= abandonedAfter;

    /// <summary>
    /// Replaces the file's tags with the given set (total replacement; see <see cref="TagSet.Replace"/>).
    /// </summary>
    public void SetTags(IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        _tags.Replace(tags.Select(Tag.Create));
    }
}

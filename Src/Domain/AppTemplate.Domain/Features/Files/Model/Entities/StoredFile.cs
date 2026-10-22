using AppTemplate.Domain.Common.Tagging;
using AppTemplate.Domain.Core.Common.Abstractions;
using AppTemplate.Domain.Core.Common.Exceptions;
using AppTemplate.Domain.Core.Common.Primitives;
using AppTemplate.Domain.Features.Files.Model.Events;
using AppTemplate.Domain.Features.Files.Model.ValueObjects;

namespace AppTemplate.Domain.Features.Files.Model.Entities;

public sealed class StoredFile : AuditableAggregateRoot<Guid>, IOwnedAggregate
{
    public const int MaxTags = 20;

    private static readonly TimeSpan _firstInspectionRetryDelay = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan _longestInspectionRetryDelay = TimeSpan.FromHours(1);

    private readonly TagSet _tags = new(MaxTags, "stored file");

    private StoredFile(
        Guid id,
        UserId ownerId,
        FileCategory category,
        ObjectKey objectKey,
        StoredFileDeclaration declaration,
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
        ArgumentNullException.ThrowIfNull(declaration);

        if (registeredAt == default)
        {
            throw new DomainException("A stored file must record when it was registered.");
        }

        OwnerId = ownerId;
        Category = category;
        ObjectKey = objectKey;
        Name = declaration.Name;
        DeclaredMediaType = declaration.MediaType;
        Size = declaration.Size;
        Checksum = declaration.Checksum;
        RegisteredAt = registeredAt;
    }

    public IReadOnlyCollection<Tag> Tags => _tags.Tags;

    public UserId OwnerId { get; private set; }

    public FileCategory Category { get; private set; }

    public ObjectKey ObjectKey { get; private set; }

    public StoredFileName Name { get; private set; }

    public DeclaredMediaType DeclaredMediaType { get; private set; }

    public FileSize Size { get; private set; }

    public Sha256Checksum Checksum { get; private set; }

    public StoredFileState State { get; private set; } = StoredFileState.Pending;

    public DateTimeOffset RegisteredAt { get; private set; }

    public DateTimeOffset? AvailableAt { get; private set; }

    public int InspectionAttempts { get; private set; }

    public DateTimeOffset? NextInspectionAt { get; private set; }

    public static StoredFile Register(
        UserId ownerId,
        FileCategory category,
        StoredFileDeclaration declaration,
        DateTimeOffset now) =>
        new(Guid.CreateVersion7(), ownerId, category, ObjectKey.New(now), declaration, now);

    public static StoredFile Rehydrate(StoredFileSnapshot stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(stored.Tags);

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
            StoredFileDeclaration.Create(stored.Name, stored.DeclaredMediaType, stored.Size, stored.Checksum),
            stored.RegisteredAt)
        {
            State = stored.State,
            AvailableAt = stored.AvailableAt,
            InspectionAttempts = stored.InspectionAttempts,
            NextInspectionAt = stored.NextInspectionAt,
        };

        file.SetTags(stored.Tags);

        return file;
    }

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

    public void Quarantine(DateTimeOffset now)
    {
        if (State != StoredFileState.Deposited)
        {
            throw new DomainException("Only a stored file with a confirmed deposit can be quarantined.");
        }

        State = StoredFileState.Quarantined;

        RaiseDomainEvent(new StoredFileQuarantinedDomainEvent(Id, OwnerId, ObjectKey, now));
    }

    public void DeferInspection(DateTimeOffset now)
    {
        if (State != StoredFileState.Deposited)
        {
            throw new DomainException("Only a deposited stored file can have its inspection deferred.");
        }

        InspectionAttempts++;
        NextInspectionAt = now + InspectionRetryDelayAfter(InspectionAttempts);
    }

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

    public void Delete(DateTimeOffset now)
    {
        if (DomainEvents.OfType<StoredFileDeletedDomainEvent>().Any())
        {
            throw new DomainException("This stored file has already been deleted in this unit of work.");
        }

        RaiseDomainEvent(new StoredFileDeletedDomainEvent(Id, OwnerId, ObjectKey, now));
    }

    public bool IsAbandoned(DateTimeOffset now, TimeSpan abandonedAfter) =>
        State == StoredFileState.Pending && now - RegisteredAt >= abandonedAfter;

    public void SetTags(IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        _tags.Replace(tags.Select(Tag.Create));
    }
}

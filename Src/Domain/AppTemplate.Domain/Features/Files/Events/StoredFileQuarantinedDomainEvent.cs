using AppTemplate.Domain.Common.Events;
using AppTemplate.Domain.Features.Files.ValueObjects;

namespace AppTemplate.Domain.Features.Files.Events;

/// <summary>
/// Raised when a file's content was examined and refused. The file is not servable, was never
/// servable, and never will be.
/// <para>
/// <b>It carries no reason, and that is the same decision <see cref="StoredFileState.Quarantined"/>
/// records.</b> Which detector fired is an operator's fact, logged where a malware signature can be
/// named; an event is a fact the domain publishes to anything at all, and a project that routes it
/// to the uploader would be handing an attacker a test harness. What a consumer is entitled to know
/// is that this file was refused.
/// </para>
/// <para>
/// <b>Shaped for a delivery that may not happen</b>, like every event here: dispatched in-process,
/// after commit, at most once, with no outbox. Nothing about the refusal depends on it arriving —
/// the state is committed on the row before this is dispatched, and the state is what every read
/// consults. A consumer that notifies the owner or raises an alert is free to be written; one that
/// is what makes the file unservable is not, and could not be, since the file is already unservable
/// by the time this exists.
/// </para>
/// <para>
/// <see cref="ObjectKey"/> travels with it because the one thing a consumer might reasonably want to
/// do is get rid of the bytes. Doing so is a decision with a cost: the row still names this key, so
/// the orphan sweep will not reclaim these bytes on its own, and deleting them destroys the only
/// sample an investigation could look at.
/// </para>
/// </summary>
public sealed record StoredFileQuarantinedDomainEvent(
    Guid StoredFileId,
    Guid OwnerId,
    ObjectKey ObjectKey,
    DateTimeOffset OccurredOn) : IDomainEvent;

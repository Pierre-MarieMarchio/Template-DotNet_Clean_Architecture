# 0026 — Correctness does not depend on event delivery

Status: Accepted (amends [0017](0017-no-outbox-for-domain-events.md))

## Context

[0017](0017-no-outbox-for-domain-events.md) refuses a transactional outbox, and one of its
supports was that losing a domain event could not be observed: the only consumer wrote a log
line. That support is gone. A reminder is cancelled when its to-do item is completed, and a
cancellation that never happens is not a missing log line — the owner is told to do something
they have already done.

The delivery window is real and narrow.
`DomainEventDispatchSaveChangesInterceptor` collects events before the save and dispatches them
from `SavedChangesAsync`, after the transaction has committed. A process that dies in between
loses every consumer for that save, and nothing durable recorded that the event was raised.

0017's own "revisit when" clause names the trigger as a consumer performing *a side effect a user
would notice missing*. Read to the letter it does not fire here: this consumer writes a row in
the application's own database, and the thing a user notices is produced later, by a background
host that consumes no events at all. Read for what it is trying to test — can the loss be
observed — it fires plainly.

The letter is the wrong reading, and not merely because the outcome is inconvenient. A condition
that requires the consumer to be the actuator can be defused by putting a table between the
consumer and the effect, which is what an outbox does. A trigger that stops applying once you
apply the remedy it exists to trigger is not a usable test.

## Decision

**An event may be lost without the system becoming wrong.** Delivery is an optimisation, and the
three pieces that make that true are all in the reminder feature:

- **The effect re-derives its own precondition.** `FireDueRemindersUseCase` asks
  `IReminderTargets` whether each due reminder's item is still outstanding, and cancels instead of
  notifying when it is not. The answer comes from the store at the moment of the effect, so it
  does not matter whether an event arrived earlier.
- **Consumers are idempotent by shape.** `Reminder.Cancel` assigns a state rather than moving one,
  so a redelivered cancellation is a no-op with no de-duplication table behind it.
- **The divergence is counted.** When the re-derivation finds a due reminder whose item is already
  complete, that is exactly one lost event, and `IReminderDiagnostics.RecordMissedCancellation`
  records it. The residual risk 0017 documents in prose now has a number.

**This works only where the effect is produced by something that re-reads state at the moment it
acts.** A reminder is polled, so such a moment exists. A welcome email sent *now* has no later
instant under our control, and a consumer of that shape still needs an outbox — the technique
here is the first thing to try, not a general replacement.

## Consequences

- 0017's decision stands, for a different reason: not because losing an event is unobservable,
  but because nothing depends on it having arrived.
- The consumer on `TodoItemCompletedDomainEvent` is a **fast path**, not a guarantee. It keeps the
  table tidy and retires a reminder before its due date rather than at it. Deleting it would not
  make the system incorrect, only slower to settle — which is why it says so in its own summary.
- **Deletion propagates without an event.** The domain raises nothing when an item or a list is
  removed, and it does not need to: an item missing from `IReminderTargets`' projection is an item
  that no longer exists, and its reminders are retired at their first due date. Two domain events
  that would otherwise have been required are not written.
- Notification is **at-least-once**. A crash between sending and recording `NotifiedAt` fires the
  reminder twice. For a reminder a duplicate is mild and a silent omission is not; a feature where
  that trade runs the other way must not copy this shape without revisiting it.
- `SECURITY.md`'s known-gap entry no longer tells a reader to add an outbox as soon as a consumer's
  loss would be noticed. It names the compensating mechanism, and keeps the instruction for the
  case the mechanism cannot cover.

## Alternatives rejected

- **Ship the outbox.** It would make delivery reliable without making the reminder correct: a
  cancellation delivered a minute late still races the due date, so the firing path would have to
  re-check anyway. Paying for a table, a dispatcher and a dead-letter path to keep a check we
  cannot remove is the expensive half of the answer. 0017 already sets out why the operational
  half does not belong to a template.
- **Store nothing and derive everything.** If "is this reminder still wanted" were computed purely
  from the item, there would be no consumer, no event, and nothing to lose — strictly the most
  correct design. It is rejected because a reminder has a life of its own: it can be rescheduled
  or dismissed while its item stays open, and that state has to live somewhere.
- **Tolerate the loss and document it.** Idempotent cancellation on its own makes a redelivery
  safe without providing one. It is a prerequisite here, not an answer.

## Revisit when

An effect must be produced at a moment when the state behind it cannot be re-read — mail leaving
the process, a payment, a call to a third party — or the missed-cancellation counter is non-zero
more often than the deployment is willing to accept. The first is a design fact, the second is a
measurement, and either is enough to reopen 0017 with the outbox back on the table.

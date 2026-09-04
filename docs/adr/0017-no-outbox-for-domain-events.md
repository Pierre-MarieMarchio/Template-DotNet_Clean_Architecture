# 0017 — No outbox for domain events

Status: Accepted

## Context

`DomainEventDispatchSaveChangesInterceptor`
(`Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Common/DomainEvents/DomainEventDispatchSaveChangesInterceptor.cs`)
collects events during `SavingChanges`/`SavingChangesAsync` and dispatches them from
`SavedChanges`/`SavedChangesAsync` — after the transaction has committed, never before, so
a consumer cannot observe a change that then rolls back. `DomainEventDispatcher`
(`DomainEventDispatcher.cs` in the same folder) resolves each event's consumers from the
container and awaits them in a loop.

Consumer isolation is being narrowed alongside this record: today, one consumer throwing
stops the remaining consumers *of that same event* from running at all; the change under
way makes each consumer's failure independent of its siblings, so the rest still run. That
closes one gap and leaves the next one exactly where it was — the side effect of the
consumer that threw is still lost, logged and not retried, and a process that dies between
commit and dispatch loses every consumer for that save, because nothing durable recorded
that the event was ever raised. An outbox — a table written in the same transaction as the
aggregate, plus a separate dispatcher that reads and retries it — is what closes that
remaining gap. It is refused here.

## Decision

**Domain events are dispatched in-process, best-effort, after commit, with no outbox.**
`SECURITY.md`'s "Known gaps" section states the residual risk rather than hiding it.

## Consequences

- A consumer's side effect can be silently lost: on a throw (even with per-consumer
  isolation, the thrower's own work is gone), and on a process crash between the commit
  and the dispatch loop.
- Nothing here requires a consumer to be idempotent. That is the trade, not an oversight:
  an outbox changes the delivery guarantee to at-least-once, and at-least-once is a
  contract on every consumer, forever — redelivery becomes the normal case a consumer
  author must design for, not an edge case.
- Shipping the table without the operational half — a dispatcher process, a poison-message
  or dead-letter path, and monitoring of dispatch lag — would produce something that looks
  like at-least-once delivery and is not. None of those three has a correct default a
  template can ship; each is a deployment's own infrastructure decision.
- A project that needs the stronger guarantee adds the outbox table, a dispatcher, and
  makes its consumers idempotent as a deliberate decision made with its own operational
  characteristics in view — not as something inherited unexamined from a template default.
- `SECURITY.md`'s known-gaps entry narrows to reflect per-consumer isolation, but does not
  close: the process-crash window and the thrower's own lost side effect remain.

## Alternatives rejected

- **Ship the outbox table and dispatcher now.** The mechanism a template can version-control
  is the easy half. The half that makes it correct — a running dispatcher, alerting on
  lag, a dead-letter path, and every consumer author briefed that redelivery is normal —
  is operational and belongs to whoever deploys this, not to the template.
- **A distributed transaction across the database and a message broker.** Trades one hard
  problem for a second infrastructure dependency and a two-phase commit, to guarantee
  something an outbox already guarantees with one extra table.
- **Retry the dispatch loop in-process before giving up.** Narrows the window without
  closing it — a retry loop still runs inside the same process that might crash mid-loop,
  and does nothing for the crash case at all.

## Revisit when

A consumer performs a side effect a user would notice missing — moving money, sending
mail, or anything visible outside the process. At that point the residual gap this record
describes stops being acceptable for that specific consumer, and it is the point to add
the outbox rather than tolerate best-effort delivery.

[0026](0026-correctness-does-not-depend-on-event-delivery.md) amends this clause. Reaching it
means reopening the question, not that the outbox is the answer: restructuring so that the
effect re-derives its own precondition removes the dependency on delivery altogether, and is
cheaper wherever the effect is produced by something that re-reads state when it acts. That
record also restates the trigger in a form that can be observed rather than judged.

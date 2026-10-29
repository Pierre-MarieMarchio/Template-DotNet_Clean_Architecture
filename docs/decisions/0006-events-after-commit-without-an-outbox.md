# 0006. Events after commit, without an outbox

Status: Accepted

## Context

A domain event announces a fact so other parts of the system can react. Delivery can be made
reliable with an outbox: a table written in the same transaction, a relay that reads it, a consumer
that de-duplicates. That is a second persistence path to run and watch, for a process that publishes
and consumes in memory. Without one, a crash between the commit and the consumer loses the event.

## Decision

Events are collected before the save and published in process after the commit, at most once, with
no outbox and no retry. A consumer that throws is logged and skipped; its siblings still run. A fact
no aggregate carries is published by the use case or service that made it true, after its write
commits, never before.

What makes this safe is the shape every consumer must have:

- **It re-derives its precondition** when it runs, so running twice is the same as running once, and
  never running leaves the system consistent but stale.
<!--#if (!jobsWorker)-->
- **A sweep is the guarantee** for a fact whose loss would matter: a periodic pass under a lease
  ([ADR 0011](0011-jobs-run-in-the-api-under-a-lease.md)) that re-derives the same effect, so the
  event only makes it prompt.
<!--#endif-->
<!--#if (jobsWorker)-->
- **A sweep is the guarantee** for a fact whose loss would matter: a periodic pass under a lease
  ([ADR 0025](0025-jobs-run-in-a-worker-under-a-lease.md)) that re-derives the same effect, so the
  event only makes it prompt.
<!--#endif-->
- **Every event is consumed or listed** as unconsumed with its reason.

## Consequences

- A lost event costs an interval, never correctness, and there is no relay, outbox table or
  de-duplication store to operate.
- A consumer added without that shape turns the missing outbox from a cost into a bug. Reviewing a
  new consumer means finding its precondition and its sweep.
- `DomainEventTests` relates the events raised to the consumers registered, in both directions.
<!--#if (anyFeature)-->
- Account deletion follows this shape: a consumer per feature and a leased sweep
  ([ADR 0012](0012-account-deletion-each-feature-decides.md)).
<!--#endif-->
<!--#if (files)-->
- Files reclaims unreferenced bytes by a sweep; the deletion event only shortens the wait.
<!--#endif-->

[Domain events after the commit](../architecture/domain-and-results.md#domain-events-after-the-commit)
describes publication and the consumer contract.

## Revisit when

A consumer's effect cannot be re-derived by a sweep, such as a message to another system that must
leave exactly once, or the events are needed outside this process.

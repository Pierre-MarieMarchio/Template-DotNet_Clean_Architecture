# 0011. Jobs run in the API, under a lease

Status: Accepted

## Context

A service has periodic work: purging expired rows, sweeping what an event missed, passes over
pending items. A separate worker process is a second image, a second deployment and a second
composition to keep in step with the API's. Running jobs inside the API means several instances
run them, and two instances must not run the same pass at once.

## Decision

- **In the API process.** Each job is a timer hosted by the API. `Jobs:Enabled` switches every job
  of an instance on or off, and each job has its own switch and interval.
- **A trigger, not a guard.** A job's background service only starts its pass. The operation that
  must not run twice takes `ILeaderLease` inside its use case, so every caller is excluded, a timer
  or an HTTP request alike.
- **The lease is a PostgreSQL advisory lock.** It is a session-level lock on a connection of its
  own, outside the pool, keyed by a stable hash of the lease's name. A crashed process releases it;
  nothing waits for a timeout. An instance that does not get the lease skips the pass.
- **It is not a fencing token.** Leadership can be lost mid-run, so every leased operation must
  survive a second instance running it too: re-derive, never assume
  ([ADR 0006](0006-events-after-commit-without-an-outbox.md)).

## Consequences

- One image and one deployment. A team that wants jobs apart from HTTP runs the same image with
  `Jobs:Enabled` false on the HTTP instances and true on one other.
- A lease name is part of the deployment: two releases running side by side during a rolling deploy
  must spell it the same, or both run the pass.
- `BackgroundWorkTests` holds that no background service takes the lease and that a use case does;
  `LeaderLeaseTests` proves the exclusion on a real PostgreSQL.
- A pass that is idempotent by shape, such as a purge of expired rows in bounded batches, takes no
  lease at all.

[Background jobs](../features/background-jobs.md) lists the jobs and their settings, and
[Leases](../features/background-jobs.md#leases) the lease's contract.

## Revisit when

Jobs compete with HTTP for the same resources under load, need scheduling beyond intervals
(calendars, retries, a dashboard), or a pass must be proven to run on one instance only.

# 0025. Jobs run in a worker, under a lease

Status: Accepted

## Context

A service has periodic work: purging expired rows, sweeping what an event missed, passes over
pending items. Inside the API, a pass shares the process with HTTP: its threads, its connections,
its memory and its image, which then carries every dependency a pass needs. Separating the two by
running the same image twice, with jobs switched off on the HTTP instances, still starts the HTTP
pipeline in a process that serves no request. A process of its own costs a second composition, a
second image and a second deployment to keep in step with the API's
([ADR 0022](0022-a-presentation-project-reaches-the-core-through-the-api-or-the-application-layer.md)),
and two instances of it must still not run the same pass at once.

## Decision

- **In the worker process.** `<Project>.Worker` is a generic host, on `Microsoft.NET.Sdk.Worker`, that
  composes the features and modules the passes call and hosts every job: the timers, `Jobs:Enabled`,
  each job's switch and interval. The API composes no job and binds no `Jobs` section;
  `TheJobs_RunInOneHost` holds that exactly one host calls `AddBackgroundJobs`.
- **No caller, no HTTP.** Every scope the worker opens is a background scope: `ICurrentUser` names no
  one and the audit actor records nobody, so a use case that needs a caller fails loudly there. The
  worker serves no route and no health endpoint; its log and the `iterations` counter of each loop
  say whether it runs.
- **Migrations stay with the API.** In Development the API migrates its database and the worker waits
  for it to answer; at release the migration bundles run before either starts.
- **A trigger, not a guard.** A job's background service only starts its pass. The operation that
  must not run twice takes `ILeaderLease` inside its use case, so the worker's timer and the API's
  maintenance routes are excluded alike.
<!--#if (postgreSql)-->
- **The lease is a PostgreSQL advisory lock.** It is a session-level lock on a connection of its
  own, outside the pool, keyed by a stable hash of the lease's name. A crashed process releases it;
  nothing waits for a timeout. An instance that does not get the lease skips the pass.
<!--#endif-->
<!--#if (sqlServer)-->
- **The lease is a SQL Server application lock.** It is a session-owned `sp_getapplock` on a
  connection of its own, outside the pool, named by the lease's name. A crashed process releases it;
  nothing waits for a timeout. An instance that does not get the lease skips the pass.
<!--#endif-->
- **It is not a fencing token.** Leadership can be lost mid-run, so every leased operation must
  survive a second instance running it too: re-derive, never assume
  ([ADR 0006](0006-events-after-commit-without-an-outbox.md)).

## Alternatives considered

- **The jobs in the API, one image run in two roles.** One deployment, but every pass's dependencies
  ship with the HTTP instances, and the jobs instance starts an HTTP pipeline it never serves.
- **A worker that references the API project.** No host references another; the worker would start
  with the HTTP composition's registrations and depend on a project that changes for reasons of its
  own.
- **A jobs library both hosts could reference.** A project whose only purpose is to move between
  hosts; a job is a host's composition of a use case, and what hosts share goes to the layer where
  it belongs.
- **A scheduler such as Quartz.NET or Hangfire.** Calendars, retries and a dashboard that interval
  passes do not need, with tables of their own to migrate.

## Consequences

- Two images and two deployments, released from the same commit: a pass and the route that shares
  its lease must spell the lease's name the same, or both run during a rolling deployment.
- Scaling the API never multiplies the passes. One worker instance runs them; a second one is stood
  by by the lease, which is not a reason to run two.
- A setting a pass reads is set on the worker; a setting a use case reads is set on every host that
  composes it.
- `new-feature` writes the deleted-account sweep's jobs into the worker and prints the feature's
  registration for it, as for every host that composes features.
- `WorkerCompositionTests` builds the worker from its own `Program.cs` as Development does, with
  every registration validated, resolves each use case it composes in a background scope, and holds
  that it runs the jobs and no server; `BackgroundWorkTests` reads the worker's jobs;
  `LeaderLeaseTests` proves the exclusion.

[Background jobs](../features/background-jobs.md) lists the jobs and their settings.

## Revisit when

Passes need scheduling beyond intervals (calendars, retries, a dashboard), or the worker and the API
compose so nearly the same thing that keeping the two in step costs more than the separation buys.

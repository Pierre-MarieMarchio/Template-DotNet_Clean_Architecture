# Background jobs

What this project runs on a timer, in which process, and how two instances avoid doing the same
work twice.

## The jobs

| Job | Pass | Default interval |
|---|---|---|
| Maintenance | purges expired idempotency keys | 1 hour |
<!--#if (localAuth)-->
| Maintenance | purges expired refresh tokens, and security events past their retention | 1 hour |
<!--#endif-->
<!--#if (anyFeature)-->
| Maintenance | sweeps the data deleted accounts still own ([Accounts](accounts.md#deleting-an-account)) | 1 hour |
<!--#endif-->
<!--#if (reminders)-->
| Reminders | sends the reminders that fell due | 1 minute |
<!--#endif-->
<!--#if (files)-->
| Files | inspects deposited files, purges abandoned registrations, reclaims orphaned objects ([Files](files.md#background-passes)) | 1 minute, 1 hour, 12 hours |
<!--#endif-->

Every pass calls an application use case, exactly as a controller would; a job adds a trigger, not
logic.
<!--#if (anyPresentation)-->
The jobs run in the API alone: the presentation project composes the same use cases and runs none
of them on a timer.
<!--#endif-->
A pass that throws is logged and retried at the next tick, never stops the host, and never stops its
sibling tasks in the same tick.
<!--#if (accounts)-->
The maintenance purges are also routes under `/api/v1/maintenance`, for an outside scheduler,
behind the `Administrator` policy.
<!--#endif-->

## One process: HTTP and jobs

`AppTemplate.Api` serves HTTP and runs the jobs, composed by the one call `AddBackgroundJobs` in
`Src/Presentation/AppTemplate.Api/Features/BackgroundJobs/Jobs/BackgroundJobsModule.cs`. Each loop
runs on `PeriodicJob.RunAsync`
(`Src/Presentation/AppTemplate.Presentation.Core/Common/Jobs/PeriodicJob.cs`): one pass at once,
then one per interval, ticks that elapse during a slow pass coalesced, and an immediate return on
shutdown.

A pass opens its own scope with `CreateBackgroundScope()`. In that scope `ICurrentUser.UserId`
throws, since no one is calling, and the audit actor records nobody, since no one made the write.
A use case that needs a caller fails loudly there instead of acting for no one.

## Leases

<!--#if (postgreSql)-->
A pass that must not run twice at once takes `ILeaderLease` inside its use case: a PostgreSQL
advisory lock held on a dedicated, unpooled connection for the duration of the pass.
<!--#endif-->
<!--#if (sqlServer)-->
A pass that must not run twice at once takes `ILeaderLease` inside its use case: a SQL Server
application lock (`sp_getapplock`, owned by the session) held on a dedicated, unpooled connection for
the duration of the pass.
<!--#endif-->
<!--#if (sqlite)-->
A pass that must not run twice at once takes `ILeaderLease` inside its use case: an in-process lock
held for the duration of the pass, since SQLite is a file for one instance.
<!--#endif-->
A second instance's pass logs that it stood by and reports zero.
<!--#if (!anyFeature)-->
No pass of this project takes one yet: the maintenance purges are idempotent.
<!--#endif-->
<!--#if (anyFeature)-->

| Lease | Taken by |
|---|---|
<!--#if (anyFeature)-->
| `apptemplate.accounts.sweep-deleted-data` | the deleted-account sweep |
<!--#endif-->
<!--#if (reminders)-->
| `apptemplate.reminders.fire-due` | the reminder pass |
<!--#endif-->
<!--#if (files)-->
| `apptemplate.files.inspect-deposited`, `apptemplate.files.purge-abandoned`, `apptemplate.files.reclaim-orphans` | the three file passes |
<!--#endif-->
<!--#endif-->

A lease is not a fencing token: leadership can lapse mid-pass without the pass knowing. Each
leased pass therefore states what a repeat costs, and the other passes are idempotent.
<!--#if (reminders)-->
The reminder pass sends, then commits, one reminder at a time, so a crash between the two sends
that one reminder twice: delivery is at least once. A failed send is retried after a minute,
doubling up to an hour, behind the reminders that waited longer.
<!--#endif-->

<!--#if (postgreSql)-->
Each leased pass holds one connection beyond `Database:MaxPoolSize`. A pooler in transaction mode
(pgBouncer) breaks the advisory lock silently; give the lease a session-mode or direct route.
<!--#endif-->
<!--#if (sqlServer)-->
Each leased pass holds one connection beyond `Database:MaxPoolSize`. The application lock belongs to
that session, so a proxy that multiplexes sessions breaks it silently; give the lease a direct route.
<!--#endif-->
<!--#if (sqlite)-->
A lease excludes only the callers of its own process, so a second instance is not stood by; this
project runs as one instance, which [ADR 0021](../decisions/0021-sqlite-is-a-development-database.md)
explains.
<!--#endif-->

## Several instances

`Jobs:Enabled` is an instance's role, not a job setting. Leave it `true` on exactly one instance
and set `Jobs__Enabled=false` on the others: they serve every route, start no loop and bind no job
section. The leases and idempotent passes cover what still overlaps, a rolling deployment or a
mistaken double enable, but they are not a substitute for that choice.

`/health/ready` says nothing about the jobs. The `iterations` counter of each loop, tagged with its
`outcome`, does ([Observability](observability.md#metrics)): a flat series is a loop that stopped.

## Add a job

**A purge or a sweep joins the maintenance timer**: a use case returning the count it removed,
registered with `AddMaintenanceTask`, internal to `AppTemplate.Api`, from the feature's own
`Features/<Feature>/Jobs/` folder, with its own switch:

```csharp
services.AddMaintenanceTask<IPurgeExpiredExportsUseCase>(
    "expired exports",
    "Expired exports will accumulate: nothing else removes them.",
    provider => provider.GetRequiredService<IOptions<JobsExportsOptions>>().Value.PurgeEnabled);
```

`Src/Presentation/AppTemplate.Api/Features/Maintenance/Jobs/MaintenanceJobsExtensions.cs` registers
the idempotency purge this way.

**A pass with its own interval is a `BackgroundService`** running `PeriodicJob.RunAsync`, opening
each pass with `CreateBackgroundScope()`, with options under `Jobs:<Feature>` bound and validated
only when `Jobs:Enabled` is true, a meter counting `iterations`, and one call from
`AddBackgroundJobs`.
<!--#if (reminders)-->
`Src/Presentation/AppTemplate.Api/Features/Reminders/Jobs/ReminderJobsExtensions.cs` and the
`ReminderBackgroundService` beside it are the model.
<!--#endif-->
<!--#if (!reminders)-->
`MaintenanceBackgroundService`, in `Src/Presentation/AppTemplate.Api/Features/Maintenance/Jobs/`, is
the model.
<!--#endif-->

Either way, a pass that must run on one instance takes `ILeaderLease` in its use case, not in the
loop.

## Configure

| Section | What it sets |
|---|---|
| [`Jobs`](../reference/configuration.md#jobs) | `Enabled`: whether this instance runs jobs at all |
| [`Jobs:Maintenance`](../reference/configuration.md#jobsmaintenance) | the maintenance interval and each task's switch |
<!--#if (reminders)-->
| [`Jobs:Reminders`](../reference/configuration.md#jobsreminders) | the reminder interval and switch |
<!--#endif-->
<!--#if (files)-->
| [`Jobs:Files`](../reference/configuration.md#jobsfiles) | each file pass's interval and switch |
<!--#endif-->
| [`IdempotencyPurge`](../reference/configuration.md#idempotencypurge) | rows deleted per round trip by the idempotency purge |

An invalid interval fails start-up on an instance that runs jobs; it is never read as "disabled".
A maintenance task switched off logs a warning on every tick saying what accumulates.

## Decisions

- [0011 Jobs run in the API under a lease](../decisions/0011-jobs-run-in-the-api-under-a-lease.md)
- [0006 Events after commit, without an outbox](../decisions/0006-events-after-commit-without-an-outbox.md): why a sweep backs every event.

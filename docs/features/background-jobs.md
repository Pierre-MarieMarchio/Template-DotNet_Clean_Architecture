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

Every pass sends an application use case's message through `ISender`, exactly as a controller does;
a job adds a trigger, not logic.
<!--#if (!jobsWorker)-->
The jobs run in the API alone: any other host composes the same use cases and runs none of them on
a timer.
<!--#endif-->
<!--#if (jobsWorker && api)-->
The jobs run in the worker alone: the API and every other host compose the same use cases and run
none of them on a timer.
<!--#endif-->
<!--#if (jobsWorker && !api)-->
The jobs run in the worker alone: every other host composes the same use cases and runs none of
them on a timer.
<!--#endif-->
A pass that throws is logged and retried at the next tick, never stops the host, and never stops its
sibling tasks in the same tick.
<!--#if (accounts)-->
The maintenance purges are also routes under `/api/v1/maintenance`, for an outside scheduler,
behind the `Administrator` policy.
<!--#endif-->

<!--#if (!jobsWorker)-->
## One process: HTTP and jobs

`AppTemplate.Api` serves HTTP and runs the jobs, composed by the one call `AddBackgroundJobs` in
`Src/Presentation/AT.Api/Features/BackgroundJobs/Jobs/BackgroundJobsModule.cs`. Each loop
runs on `PeriodicJob.RunAsync`
(`Src/Presentation/AT.Presentation.Core/Common/Jobs/PeriodicJob.cs`): one pass at once,
then one per interval, ticks that elapse during a slow pass coalesced, and an immediate return on
shutdown.
<!--#endif-->
<!--#if (jobsWorker)-->
## The worker

`Src/Presentation/<Initials>.Worker` is a generic host that runs the jobs and serves no HTTP
([ADR 0025](../decisions/0025-jobs-run-in-a-worker-under-a-lease.md)). Its `Program.cs` composes the
features and the modules the passes call, as the API's does, gives every scope no caller, and calls
`AddBackgroundJobs` from `Features/BackgroundJobs/Jobs/BackgroundJobsModule.cs`; each feature's jobs
are under its own `Features/<Feature>/Jobs/`.
<!--#if (api)-->
The API composes no job and binds no `Jobs` section, and keeps the maintenance routes.
<!--#endif-->
Each loop runs on `PeriodicJob.RunAsync`
(`Src/Presentation/AT.Presentation.Core/Common/Jobs/PeriodicJob.cs`): one pass at once,
then one per interval, ticks that elapse during a slow pass coalesced, and an immediate return on
shutdown.

<!--#if (api)-->
The worker migrates nothing. In Development the API migrates its database as it starts, and Compose
starts the worker once the API answers; at release the migration bundles run before either host.
<!--#endif-->
<!--#if (!api)-->
The worker migrates nothing, and neither does any other host of this project: in Development the
migrations are applied with `dotnet ef database update`, as [first run](../getting-started/first-run.md#run-with-compose)
shows, and at release the migration bundles run before the hosts.
<!--#endif-->
Its tests are in `Tests/Presentation/<Initials>.Worker.UnitTests`: `WorkerCompositionTests` builds the
worker from its own `Program.cs` with every registration validated and resolves each use case's
handler it composes in a background scope.
<!--#endif-->

Every pass sends its command and records what came of it through one `JobPass`, in the jobs host's
`Common/Jobs/`: a span from its feature's activity source, one `iterations` count tagged with its
`task` and its `outcome`, the count its handler returns, and a log line. A failure or an exception
is logged and counted, never thrown; a stop that lands mid-pass is not a failure. A pass on an
interval of its own runs in `IntervalPassBackgroundService<TCommand>` beside it; the maintenance
tasks run one after another in `MaintenanceBackgroundService`, on one timer and in one scope.

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
doubling up to an hour, behind the reminders that waited longer: `RetryBackoff`, in
`Src/Domain/AT.Domain.Core/Common/Retries/RetryBackoff.cs`, holds that wait.
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

<!--#if (!jobsWorker)-->
`Jobs:Enabled` is an instance's role, not a job setting. Leave it `true` on exactly one instance
and set `Jobs__Enabled=false` on the others: they serve every route, start no loop and bind no job
section. The leases and idempotent passes cover what still overlaps, a rolling deployment or a
mistaken double enable, but they are not a substitute for that choice.

`/health/ready` says nothing about the jobs. The `iterations` counter of each loop, tagged with its
`outcome`, does ([Observability](observability.md#metrics)): a flat series is a loop that stopped.

A process of its own for the jobs is the worker, a host beside the API that composes the same
features and modules, gives every scope no caller and calls `AddBackgroundJobs`. On a committed tree,
`dotnet new cleanarch-presentation -n Worker --framework worker --project-name AppTemplate` writes it,
then `dotnet run Tools/Tasks.cs presentation-join AppTemplate.Worker` moves the API's `Common/Jobs`
and `Features/*/Jobs` folders and their tests into it, their namespaces with them, with the `Jobs`
settings, and the API stops calling `AddBackgroundJobs`.
<!--#if (localAuth)-->
The authentication purges move with `AddAuthMaintenanceJobs`, which the worker calls instead.
<!--#endif-->
A project generated with `--presentation worker` starts that way, and the `README.md` the item
template writes in the worker's folder lists every other line the join changes.
<!--#endif-->
<!--#if (jobsWorker && api)-->
Scale the API freely: it runs no pass.
<!--#endif-->
<!--#if (jobsWorker)-->
Run one worker. A second worker is stood by under each lease
and repeats the idempotent passes, which the leases and the passes survive, but which is not a
reason to run two. `Jobs__Enabled=false` leaves a worker idle, binding no job section.

The worker answers no health probe. The `iterations` counter of each loop, tagged with its
`outcome`, says it runs ([Observability](observability.md#metrics)): a flat series is a loop that
stopped.
<!--#endif-->

## Add a job

**A purge or a sweep joins the maintenance timer**: a command, taking no input, whose handler returns
the count it removed, registered with `AddMaintenanceTask`, which sends it on each pass, internal to
the host that runs the jobs, from the feature's own `Features/<Feature>/Jobs/` folder, with its own
switch:

```csharp
services.AddMaintenanceTask<PurgeExpiredExportsCommand>(
    "expired exports",
    "Expired exports will accumulate: nothing else removes them.",
    provider => provider.GetRequiredService<IOptions<JobsExportsOptions>>().Value.PurgeEnabled);
```

<!--#if (!jobsWorker)-->
`Src/Presentation/AT.Api/Features/Maintenance/Jobs/MaintenanceJobsExtensions.cs` registers
the idempotency purge this way.
<!--#endif-->
<!--#if (jobsWorker)-->
`Src/Presentation/<Initials>.Worker/Features/Maintenance/Jobs/MaintenanceJobsExtensions.cs` registers
the idempotency purge this way.
<!--#endif-->

**A pass with its own interval joins `AddIntervalPass`**: a command of the same shape, and an
`IntervalPass` read from options under `Jobs:<Feature>`, bound and validated only when
`Jobs:Enabled` is true, that gives its interval, its switch and its `JobPass`: a label, what a
switched-off pass leaves undone, and the feature's activity source and counters. One call from
`AddBackgroundJobs` registers the feature; no feature writes a `BackgroundService` of its own.

```csharp
services.AddIntervalPass<SendDueDigestsCommand>(provider =>
{
    var settings = provider.GetRequiredService<IOptions<JobsDigestsOptions>>().Value;

    return new IntervalPass(_dueDigests, settings.Interval, settings.Enabled);
});
```

<!--#if (reminders)-->
`Features/Reminders/Jobs/ReminderJobsExtensions.cs`, in the host that runs the jobs, registers the
reminder pass this way.
<!--#endif-->
<!--#if (files && !reminders)-->
`Features/Files/Jobs/FileJobsExtensions.cs`, in the host that runs the jobs, registers the three file
passes this way.
<!--#endif-->
<!--#if (!reminders && !files)-->
No pass of this project runs on an interval of its own yet: `AddIntervalPass` waits in the jobs
host's `Common/Jobs/` for the first one.
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
A pass switched off logs a warning on every tick saying what it leaves undone.

## Decisions

<!--#if (!jobsWorker)-->
- [0011 Jobs run in the API under a lease](../decisions/0011-jobs-run-in-the-api-under-a-lease.md)
<!--#endif-->
<!--#if (jobsWorker)-->
- [0025 Jobs run in a worker, under a lease](../decisions/0025-jobs-run-in-a-worker-under-a-lease.md)
<!--#endif-->
- [0006 Events after commit, without an outbox](../decisions/0006-events-after-commit-without-an-outbox.md): why a sweep backs every event.

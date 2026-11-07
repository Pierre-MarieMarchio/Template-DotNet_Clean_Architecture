# AppTemplate.Worker

The host that runs the background jobs, beside the API, which runs none: a generic host on
`Microsoft.NET.Sdk.Worker` that serves no HTTP. [Background jobs](../../../docs/features/background-jobs.md)
describes it, and [ADR 0025](../../../docs/decisions/0025-jobs-run-in-a-worker-under-a-lease.md) why.

## Create the project

The folder holds the whole project, written in the shape `dotnet new worker` gives a worker at the
version scaffold.json names; no template runs. From the project root:

```bash
dotnet run Tools/Tasks.cs presentation-scaffold AppTemplate.Worker
```

`bootstrap` runs the same step. It needs no network: it removes scaffold.json, adds the project and
its tests to the solution and formats them. Commit the project: from then on the step leaves it
alone.
<!--#if (!jobsWorker)-->

## Joining a project generated without one

A project generated without the worker, whose API runs the jobs, takes it later, at the project root,
on a git tree with nothing to commit:

```bash
dotnet new cleanarch-presentation -n Worker --framework worker --project-name AppTemplate
dotnet run Tools/Tasks.cs presentation-join AppTemplate.Worker
dotnet run Tools/Tasks.cs presentation-scaffold AppTemplate.Worker
```

Pass the project's `--auth`, `--examples`, `--files`, `--database` and `--ci` too when they are not
the defaults: the project's files are written for them. The item template refuses the worker with
`--database sqlite`, whose file serves one process, and in a project without the API, whose jobs run
in a worker already.

It writes this folder, its tests and the worker's decision. `presentation-join` then moves the API's
`Common/Jobs` and `Features/*/Jobs` folders into this project, and the `Features/*/Jobs` folders of
the API's unit tests into its tests, renames each namespace they declare from the API's to the
worker's wherever the project names it, and makes the lines `--presentation worker` writes in files the item template cannot edit,
listed below, each placed by the lines it goes beside. It refuses before it writes anything while a
file it would move, edit or remove differs from what git last committed, since `git restore` is what
undoes a move; an edit in place already is left alone, and the others are made all or none. It then
builds the API and the worker: a job that uses a type only the API declares stops it there, naming
the type, which then moves to the application layer or into the job's folder. Last, it removes this
section from this README, as `--presentation worker` writes it: the files it cites have moved. Then
create the project as above and commit it.

ADR 0011 stays, superseded by ADR 0025, since it held while the API ran the jobs.
The move is safe in a rolling deployment: a pass that must not run twice takes its `ILeaderLease`
inside its use case, so the API still running the jobs and the new worker never run it together, and
the other passes are idempotent. The edits, to make by hand where the task stops:

- in `.dockerignore`, after the line:

  ```text
  !Src/Presentation/AT.Api/appsettings.json
  ```

  add:

  ```text
  !Src/Presentation/AT.Worker/appsettings.json
  ```

- in `.github/dependabot.yml`, after the line:

  ```yaml
        - /Src/Presentation/AT.Api
  ```

  add:

  ```yaml
        - /Src/Presentation/AT.Worker
  ```

<!--#if (ci)-->
- in `.github/workflows/release.yml`, replace the line:

  ```yaml
      # One image, the API, which also runs the periodic jobs.
  ```

  with:

  ```yaml
      # Two images from one commit: the API, and the worker that runs the periodic jobs.
  ```

<!--#endif-->
<!--#if (ci)-->
- in `.github/workflows/release.yml`, after the line:

  ```yaml
              dockerfile: Src/Presentation/AT.Api/Dockerfile
  ```

  add:

  ```yaml
            - component: worker
              dockerfile: Src/Presentation/AT.Worker/Dockerfile
  ```

<!--#endif-->
- in `docker-compose.yml`, remove the lines:

  ```yaml
        # false on an instance that only serves HTTP; with several instances, true on exactly one.
        Jobs__Enabled: ${JOBS_ENABLED:-true}

        Jobs__Maintenance__Interval: ${JOBS_MAINTENANCE_INTERVAL:-01:00:00}
        Jobs__Maintenance__PurgeExpiredIdempotencyKeysEnabled: ${JOBS_MAINTENANCE_PURGE_IDEMPOTENCY_KEYS:-true}
  <!--#if (localAuth)-->
        Jobs__Maintenance__PurgeExpiredRefreshTokensEnabled: ${JOBS_MAINTENANCE_PURGE_REFRESH_TOKENS:-true}
  <!--#endif-->

  <!--#if (reminders)-->
        Jobs__Reminders__Interval: ${JOBS_REMINDERS_INTERVAL:-00:01:00}
        Jobs__Reminders__Enabled: ${JOBS_REMINDERS_ENABLED:-true}
  <!--#endif-->

  <!--#if (files)-->
        # Inspection off stops the Files feature, and reclaim off leaks storage: docs/reference/configuration.md.
        Jobs__Files__InspectDepositedFilesInterval: ${JOBS_FILES_INSPECT_INTERVAL:-00:01:00}
        Jobs__Files__InspectDepositedFilesEnabled: ${JOBS_FILES_INSPECT_ENABLED:-true}
        Jobs__Files__PurgeAbandonedRegistrationsInterval: ${JOBS_FILES_PURGE_INTERVAL:-01:00:00}
        Jobs__Files__PurgeAbandonedRegistrationsEnabled: ${JOBS_FILES_PURGE_ENABLED:-true}
        Jobs__Files__ReclaimOrphanedContentInterval: ${JOBS_FILES_RECLAIM_INTERVAL:-12:00:00}
        Jobs__Files__ReclaimOrphanedContentEnabled: ${JOBS_FILES_RECLAIM_ENABLED:-true}

  <!--#endif-->
  ```

- in `docker-compose.yml`, after the lines:

  ```yaml
        retries: 5
        start_period: 60s
      networks: [backend]

  ```

  add:

  ```yaml
    worker:
      # Runs the background jobs with the hosts' settings; it serves no HTTP and migrates nothing.
      extends:
        file: docker-compose.database.yml
        service: database-client
      image: app-template-worker:dev
      build:
        context: .
        dockerfile: Src/Presentation/AT.Worker/Dockerfile
      restart: unless-stopped
      environment:
        <<: *host-environment
        DOTNET_ENVIRONMENT: ${ASPNETCORE_ENVIRONMENT:-Development}
        # One worker runs the passes; a second one stands by under each lease.
        Jobs__Enabled: ${JOBS_ENABLED:-true}

        Jobs__Maintenance__Interval: ${JOBS_MAINTENANCE_INTERVAL:-01:00:00}
        Jobs__Maintenance__PurgeExpiredIdempotencyKeysEnabled: ${JOBS_MAINTENANCE_PURGE_IDEMPOTENCY_KEYS:-true}
  <!--#if (localAuth)-->
        Jobs__Maintenance__PurgeExpiredRefreshTokensEnabled: ${JOBS_MAINTENANCE_PURGE_REFRESH_TOKENS:-true}
  <!--#endif-->

  <!--#if (reminders)-->
        Jobs__Reminders__Interval: ${JOBS_REMINDERS_INTERVAL:-00:01:00}
        Jobs__Reminders__Enabled: ${JOBS_REMINDERS_ENABLED:-true}
  <!--#endif-->

  <!--#if (files)-->
        # Inspection off stops the Files feature, and reclaim off leaks storage: docs/reference/configuration.md.
        Jobs__Files__InspectDepositedFilesInterval: ${JOBS_FILES_INSPECT_INTERVAL:-00:01:00}
        Jobs__Files__InspectDepositedFilesEnabled: ${JOBS_FILES_INSPECT_ENABLED:-true}
        Jobs__Files__PurgeAbandonedRegistrationsInterval: ${JOBS_FILES_PURGE_INTERVAL:-01:00:00}
        Jobs__Files__PurgeAbandonedRegistrationsEnabled: ${JOBS_FILES_PURGE_ENABLED:-true}
        Jobs__Files__ReclaimOrphanedContentInterval: ${JOBS_FILES_RECLAIM_INTERVAL:-12:00:00}
        Jobs__Files__ReclaimOrphanedContentEnabled: ${JOBS_FILES_RECLAIM_ENABLED:-true}

  <!--#endif-->
      depends_on:
        api:
          condition: service_healthy
      networks: [backend]

  ```

<!--#if (localAuth)-->
- in `Src/Presentation/AT.Api/Features/Auth/Composition/AuthComposition.cs`, remove the line:

  ```csharp
  using AppTemplate.Api.Features.Auth.Jobs;
  ```

<!--#endif-->
<!--#if (localAuth)-->
- in `Src/Presentation/AT.Api/Features/Auth/Composition/AuthComposition.cs`, remove the line:

  ```csharp
          services.AddAuthMaintenanceJobs(configuration);
  ```

<!--#endif-->
- in `Src/Presentation/AT.Api/Program.cs`, remove the line:

  ```csharp
  using AppTemplate.Api.Features.BackgroundJobs.Jobs;
  ```

<!--#if (anyFeature)-->
- in `Src/Presentation/AT.Api/Program.cs`, remove the lines:

  ```csharp

  builder.Services.AddDeletedAccountSweep();
  ```

<!--#endif-->
- in `Src/Presentation/AT.Api/Program.cs`, remove the lines:

  ```csharp

  builder.Services.AddBackgroundJobs(builder.Configuration);
  ```

- in `Src/Presentation/AT.Api/appsettings.json`, remove the lines:

  ```json
    "Jobs": {
      "Enabled": true,
  <!--#if (reminders)-->
      "Reminders": {
        "Interval": "00:01:00",
        "Enabled": true
      },
  <!--#endif-->
  <!--#if (files)-->
      "Files": {
        "PurgeAbandonedRegistrationsInterval": "01:00:00",
        "PurgeAbandonedRegistrationsEnabled": true,
        "ReclaimOrphanedContentInterval": "12:00:00",
        "ReclaimOrphanedContentEnabled": true,
        "InspectDepositedFilesInterval": "00:01:00",
        "InspectDepositedFilesEnabled": true
      },
  <!--#endif-->
      "Maintenance": {
        "Interval": "01:00:00",
  <!--#if (localAuth)-->
        "PurgeExpiredRefreshTokensEnabled": true,
        "PurgeExpiredSecurityEventsEnabled": true,
  <!--#endif-->
  <!--#if (anyFeature)-->
        "SweepDeletedAccountsDataEnabled": true,
  <!--#endif-->
        "PurgeExpiredIdempotencyKeysEnabled": true
      }
    },
  ```

- in `Src/Presentation/AT.Api/appsettings.Development.json`, remove the lines:

  ```json
    "Jobs": {
  <!--#if (reminders)-->
      "Reminders": {
        "Interval": "00:01:00"
      },
  <!--#endif-->
  <!--#if (files)-->
      "Files": {
        "PurgeAbandonedRegistrationsInterval": "00:01:00",
        "ReclaimOrphanedContentInterval": "00:05:00"
      },
  <!--#endif-->
      "Maintenance": {
        "Interval": "00:01:00"
      }
    },
  ```

- in `Tests/Integration/AT.Api.IntegrationTests/Infrastructure/ApiFactory.cs`, remove the line:

  ```csharp
  using AppTemplate.Api.Common.Jobs;
  ```

- in `Tests/Integration/AT.Api.IntegrationTests/Infrastructure/ApiFactory.cs`, remove the lines:

  ```csharp
              [$"{JobsOptions.SectionName}__Enabled"] = "false",

  ```

- in `Tests/Architecture/AT.Architecture.Tests/Rules/OutboundHttpTests.cs`, after the line:

  ```csharp
              "AppTemplate.Api",
  ```

  add:

  ```csharp
              "AppTemplate.Worker",
  ```

<!--#if (!publicApi)-->
- in `Tests/Architecture/AT.Architecture.Tests/Rules/LayoutConventionTests.cs`, replace the lines:

  ```csharp
                  "Controllers",
                  "Jobs",
              ],
  ```

  with:

  ```csharp
                  "Controllers",
              ],
  ```

<!--#endif-->
<!--#if (publicApi)-->
- in `Tests/Architecture/AT.Architecture.Tests/Rules/LayoutConventionTests.cs`, replace the lines:

  ```csharp
          ["Src/Presentation/AT.Api"] =
              [
                  "Composition",
  <!--#if (externalAuth)-->
                  "Configuration/Options",
  <!--#endif-->
                  "Contracts/Mapping", "Contracts/Requests", "Contracts/Responses",
                  "Controllers",
                  "Jobs",
              ],
  ```

  with:

  ```csharp
          ["Src/Presentation/AT.Api"] = null,
  ```

<!--#endif-->
<!--#if (!publicApi)-->
- in `Tests/Architecture/AT.Architecture.Tests/Rules/LayoutConventionTests.cs`, after the line:

  ```csharp
          ["Src/Infrastructure/AT.Infrastructure.Core"] = null,
  ```

  add:

  ```csharp
          ["Src/Presentation/AT.Worker"] = ["Composition", "Jobs"],
  ```

<!--#endif-->
<!--#if (publicApi)-->
- in `Tests/Architecture/AT.Architecture.Tests/Rules/LayoutConventionTests.cs`, after the line:

  ```csharp
          ["Src/Infrastructure/AT.Infrastructure.Core"] = null,
  ```

  add:

  ```csharp
          ["Src/Presentation/AT.Worker"] = ["Jobs"],
  ```

<!--#endif-->
- in `Tests/Architecture/AT.Architecture.Tests/Rules/LayoutConventionTests.cs`, replace the lines:

  ```csharp
                  "Idempotency",
                  "Jobs",
  ```

  with:

  ```csharp
                  "Idempotency",
  ```

- in `Tests/Architecture/AT.Architecture.Tests/Rules/LayoutConventionTests.cs`, after the line:

  ```csharp
              ["Jobs", "Localization", "Observability", "Outbound", "Security"],
  ```

  add:

  ```csharp
          ["Src/Presentation/AT.Worker"] = ["Jobs", "Security"],
  ```

- in `docs/architecture/README.md`, replace the line:

  ```markdown
  - [0011. Jobs run in the API under a lease](../decisions/0011-jobs-run-in-the-api-under-a-lease.md)
  ```

  with:

  ```markdown
  - [0025. Jobs run in a worker, under a lease](../decisions/0025-jobs-run-in-a-worker-under-a-lease.md)
  ```

- in `docs/architecture/shared-mechanisms.md`, replace the line:

  ```markdown
  - [0011. Jobs run in the API under a lease](../decisions/0011-jobs-run-in-the-api-under-a-lease.md)
  ```

  with:

  ```markdown
  - [0025. Jobs run in a worker, under a lease](../decisions/0025-jobs-run-in-a-worker-under-a-lease.md)
  ```

- in `docs/decisions/0006-events-after-commit-without-an-outbox.md`, replace the lines:

  ```markdown
  - **A sweep is the guarantee** for a fact whose loss would matter: a periodic pass under a lease
    ([ADR 0011](0011-jobs-run-in-the-api-under-a-lease.md)) that re-derives the same effect, so the
    event only makes it prompt.
  ```

  with:

  ```markdown
  - **A sweep is the guarantee** for a fact whose loss would matter: a periodic pass under a lease
    ([ADR 0025](0025-jobs-run-in-a-worker-under-a-lease.md)) that re-derives the same effect, so the
    event only makes it prompt.
  ```

- in `docs/decisions/0022-a-presentation-project-reaches-the-core-through-the-api-or-the-application-layer.md`, replace the lines:

  ```markdown
  - **Jobs and migrations stay in the API.** A second host runs no background job, as
    [ADR 0011](0011-jobs-run-in-the-api-under-a-lease.md) has it, and migrates nothing: the
    methods that do both live in the API, which no other host may reference. `TheJobs_RunInOneHost`
    holds that one host alone calls `AddBackgroundJobs`, the one that holds the jobs' module.
  ```

  with:

  ```markdown
  - **The worker runs the jobs, the API migrates.** The worker is the one host that runs background
    jobs, as [ADR 0025](0025-jobs-run-in-a-worker-under-a-lease.md) has it, and the API the one that
    migrates: the methods that do each live in that host, which no other host may reference.
    `TheJobs_RunInOneHost` holds that one host alone calls `AddBackgroundJobs`, the one that holds
    the jobs' module.
  ```

- in `docs/decisions/0011-jobs-run-in-the-api-under-a-lease.md`, replace the line:

  ```markdown
  Status: Accepted
  ```

  with:

  ```markdown
  Status: Superseded by [0025](0025-jobs-run-in-a-worker-under-a-lease.md)
  ```

- in `docs/decisions/0011-jobs-run-in-the-api-under-a-lease.md`, replace the line:

  ```markdown
  status: Accepted
  ```

  with:

  ```markdown
  status: Superseded
  ```

- in `docs/features/background-jobs.md`, replace the lines:

  ```markdown
  The jobs run in the API alone: any other host composes the same use cases and runs none of them on
  a timer.
  ```

  with:

  ```markdown
  The jobs run in the worker alone: the API and every other host compose the same use cases and run
  none of them on a timer.
  ```

- in `docs/features/background-jobs.md`, replace the lines:

  ```markdown
  ## One process: HTTP and jobs

  `AppTemplate.Api` serves HTTP and runs the jobs, composed by the one call `AddBackgroundJobs` in
  `Src/Presentation/AT.Api/Features/BackgroundJobs/Jobs/BackgroundJobsModule.cs`. Each loop
  runs on `PeriodicJob.RunAsync`
  (`Src/Presentation/AT.Presentation.Core/Common/Jobs/PeriodicJob.cs`): one pass at once,
  then one per interval, ticks that elapse during a slow pass coalesced, and an immediate return on
  shutdown.
  ```

  with:

  ```markdown
  ## The worker

  `Src/Presentation/<Initials>.Worker` is a generic host that runs the jobs and serves no HTTP
  ([ADR 0025](../decisions/0025-jobs-run-in-a-worker-under-a-lease.md)). Its `Program.cs` composes the
  features and the modules the passes call, as the API's does, gives every scope no caller, and calls
  `AddBackgroundJobs` from `Features/BackgroundJobs/Jobs/BackgroundJobsModule.cs`; each feature's jobs
  are under its own `Features/<Feature>/Jobs/`.
  The API composes no job and binds no `Jobs` section, and keeps the maintenance routes.
  Each loop runs on `PeriodicJob.RunAsync`
  (`Src/Presentation/AT.Presentation.Core/Common/Jobs/PeriodicJob.cs`): one pass at once,
  then one per interval, ticks that elapse during a slow pass coalesced, and an immediate return on
  shutdown.

  The worker migrates nothing. In Development the API migrates its database as it starts, and Compose
  starts the worker once the API answers; at release the migration bundles run before either host.
  <!--#if (!api)-->
  The worker migrates nothing, and neither does any other host of this project: in Development the
  migrations are applied with `dotnet ef database update`, as [first run](../getting-started/first-run.md#run-with-compose)
  shows, and at release the migration bundles run before the hosts.
  <!--#endif-->
  Its tests are in `Tests/Presentation/<Initials>.Worker.UnitTests`: `WorkerCompositionTests` builds the
  worker from its own `Program.cs` with every registration validated and resolves each use case's
  handler it composes in a background scope.
  ```

- in `docs/features/background-jobs.md`, replace the lines:

  ```markdown
  `Jobs:Enabled` is an instance's role, not a job setting. Leave it `true` on exactly one instance
  and set `Jobs__Enabled=false` on the others: they serve every route, start no loop and bind no job
  section. The leases and idempotent passes cover what still overlaps, a rolling deployment or a
  mistaken double enable, but they are not a substitute for that choice.

  `/health/ready` says nothing about the jobs. The `iterations` counter of each loop, tagged with its
  `outcome`, does ([Observability](observability.md#metrics)): a flat series is a loop that stopped.

  A process of its own for the jobs is the worker, a host beside the API that composes the same
  features and modules, gives every scope no caller and calls `AddBackgroundJobs`. On a committed tree,
  `dotnet new cleanarch-presentation -n Worker --framework worker --project-name AppTemplate` writes it,
  then `dotnet cleanarch add presentation --name Worker` moves the API's `Common/Jobs`
  and `Features/*/Jobs` folders and their tests into it, their namespaces with them, with the `Jobs`
  settings, and the API stops calling `AddBackgroundJobs`.
  <!--#if (localAuth)-->
  The authentication purges move with `AddAuthMaintenanceJobs`, which the worker calls instead.
  <!--#endif-->
  A project generated with `--presentation worker` starts that way, and the `README.md` the item
  template writes in the worker's folder lists every other line the join changes.
  ```

  with:

  ```markdown
  Scale the API freely: it runs no pass.
  Run one worker. A second worker is stood by under each lease
  and repeats the idempotent passes, which the leases and the passes survive, but which is not a
  reason to run two. `Jobs__Enabled=false` leaves a worker idle, binding no job section.

  The worker answers no health probe. The `iterations` counter of each loop, tagged with its
  `outcome`, says it runs ([Observability](observability.md#metrics)): a flat series is a loop that
  stopped.
  ```

- in `docs/features/background-jobs.md`, replace the lines:

  ```markdown
  `Src/Presentation/AT.Api/Features/Maintenance/Jobs/MaintenanceJobsExtensions.cs` registers
  the idempotency purge this way.
  ```

  with:

  ```markdown
  `Src/Presentation/<Initials>.Worker/Features/Maintenance/Jobs/MaintenanceJobsExtensions.cs` registers
  the idempotency purge this way.
  ```

- in `docs/features/background-jobs.md`, replace the line:

  ```markdown
  - [0011 Jobs run in the API under a lease](../decisions/0011-jobs-run-in-the-api-under-a-lease.md)
  ```

  with:

  ```markdown
  - [0025 Jobs run in a worker, under a lease](../decisions/0025-jobs-run-in-a-worker-under-a-lease.md)
  ```

- in `docs/getting-started/first-run.md`, before the line:

  ```markdown
  It formats the solution, writes the index of each folder under `docs/` from its pages, writes the
  ```

  add:

  ```markdown
  It also adds the worker's project in `Src/Presentation`, written in the shape of `dotnet new worker`,
  to the solution with its tests.
  ```

- in `docs/getting-started/overview.md`, before the lines:

  ```markdown

  It is a starting point. The modules and mechanisms it carries are meant to stay; the product's own
  ```

  add:

  ```markdown
  A worker beside it runs the background jobs, which the API leaves to it.
  ```

- in `docs/getting-started/overview.md`, replace the line:

  ```markdown
  | Background jobs | timers in the API process, switched per instance |
  ```

  with:

  ```markdown
  | Background jobs | timers in the worker, `Src/Presentation/<Initials>.Worker`, a process of their own |
  ```

- in `docs/getting-started/overview.md`, replace the line:

  ```markdown
  | `--presentation` | `api` | the HTTP API in `Src/Presentation`, which also runs the background jobs ([Background jobs](../features/background-jobs.md)) |
  ```

  with:

  ```markdown
  | `--presentation` | `api` | the HTTP API in `Src/Presentation` |
  | `--presentation` | `worker` | a worker in `Src/Presentation` that runs the background jobs, written in the shape of `dotnet new worker` ([Background jobs](../features/background-jobs.md)) |
  ```

- in `docs/guides/deployment.md`, replace the lines:

  ```markdown
  The API goes to an environment other than your machine. It ships as one image that serves HTTP and
  runs the periodic jobs; the schema changes in a step of its own before the image starts.
  ```

  with:

  ```markdown
  The API goes to an environment other than your machine. It ships as two images from one commit: the
  API's, which serves HTTP, and the worker's, which runs the periodic jobs; the schema changes in a step
  of its own before either starts.
  ```

- in `docs/guides/deployment.md`, before the line:

  ```markdown
  ## Migrations at release
  ```

  add:

  ```markdown
  The worker's image is built the same way, from `Src/Presentation/<Initials>.Worker/Dockerfile`, on the
  same runtime and user. It serves no port and carries no `HEALTHCHECK`: its log and its loops'
  `iterations` counter say it runs ([Background jobs](../features/background-jobs.md#several-instances)).
  Release both images together, so a pass and the route that shares its lease spell the lease's name
  the same.

  ```

<!--#if (todoLists || notes)-->
- in `docs/guides/removing-features.md`, after the line:

  ```markdown
  | `Src/Presentation/AT.Api/Program.cs` | the `Add<Features>()` line |
  ```

  add:

  ```markdown
  | `Src/Presentation/<Initials>.Worker/Program.cs` | the same line |
  ```

<!--#endif-->
<!--#if (todoLists)-->
- in `docs/guides/removing-features.md`, replace the lines:

  ```markdown
  | `Src/Presentation/AT.Api/Features/BackgroundJobs/Jobs/BackgroundJobsModule.cs` | `AddReminderJobs` |
  | `Src/Presentation/AT.Api/Features/BackgroundJobs/Jobs/BackgroundJobsTelemetry.cs` | the reminder source and meters |
  ```

  with:

  ```markdown
  | `Src/Presentation/<Initials>.Worker/Features/BackgroundJobs/Jobs/BackgroundJobsModule.cs` | `AddReminderJobs` |
  | `Src/Presentation/<Initials>.Worker/Features/BackgroundJobs/Jobs/BackgroundJobsTelemetry.cs` | the reminder source and meters |
  ```

<!--#endif-->
- in `docs/reference/configuration.md`, after the lines:

  ```markdown
  ## Background jobs

  ```

  add:

  ```markdown
  The worker reads these sections, from `Src/Presentation/<Initials>.Worker/appsettings.json` and its
  environment; the API reads none of them. The worker also reads the sections of the modules it
  composes, with the same keys as the API.

  ```

- in `docs/reference/project-layout.md`, replace the lines:

  ```text
    AT.Api                                Common/{Caching,Concurrency,Contracts,Controllers,Errors,Hosting,
                                                  Idempotency,Jobs,Localization,Observability,OpenApi,
                                                  Security}
                                          Features/<F>/{Composition,
  <!--#if (externalAuth)-->
                                                        Configuration/Options,
  <!--#endif-->
                                                        Contracts/{Mapping,Requests,Responses},
                                                        Controllers,Jobs}
  ```

  with:

  ```text
    AT.Api                                Common/{Caching,Concurrency,Contracts,Controllers,Errors,Hosting,
                                                  Idempotency,Localization,Observability,OpenApi,
                                                  Security}
                                          Features/<F>/{Composition,
  <!--#if (externalAuth)-->
                                                        Configuration/Options,
  <!--#endif-->
                                                        Contracts/{Mapping,Requests,Responses},
                                                        Controllers}
    <Initials>.Worker                     Common/{Jobs,Security}
                                          Features/<F>/{Composition,Jobs}
  ```
<!--#endif-->

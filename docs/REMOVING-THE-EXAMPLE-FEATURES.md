# Removing the example features

This template ships three worked examples: `TodoLists`, `Reminders` and `Files`. They exist to teach
— an aggregate with real invariants and child entities, a flat aggregate reached from a background
loop, an aggregate whose two halves live in two different stores, and, across all three, domain
events, optimistic concurrency over HTTP, a paginated collection endpoint and a worker calling the
same use cases as the API. A derived project deletes what it has read and keeps what it needs.

`Files` is the one where "delete it" is not the only sensible answer. Structurally it sits at
exactly the same rank as the other two — its own aggregate, its own use cases, its own controller,
its own worker loop, its own migration — so everything below gives it a column. But unlike the other
two it is also a working capability: a project that stores files re-points it at its own bucket and
its own metadata instead of deleting it. `README.md` says the same thing from the other side. So
read the `Files` column as *what it costs to remove*, not as *what you are expected to do*.

`docs/ADDING-A-FEATURE.md` says a clean removal means rewriting the tests that use an example as
their subject, rather than only deleting files. This document is that rewrite, written as a
procedure.

## What this document promises

The `Reminders`-only removal below was carried out in full in a disposable copy of this repository
and measured: three rounds of `dotnet build` (5 errors, then 1, then none), then the ten unit and
architecture test projects, all green — 2618 passing, 0 failing, 0 skipped, including
`PendingModelChangesTests`, which is what proves the migration edit. Every file named in [What to
edit](#what-to-edit) is a file that removal actually required.

Three things are **not** measured here and you should treat them as such:

- **The integration suites.** `AppTemplate.Api.IntegrationTests` and
  `AppTemplate.Infrastructure.Identity.IntegrationTests` need Docker. Everything this document says
  about them comes from reading them, not from running them. Run them yourself before you call a
  removal done.
- **The Docker image builds.** The `Dockerfile` warning below is a real property of `dotnet restore`
  and the `COPY` lines are real, but no image was built to confirm the failure mode.
- **The `TodoLists`+`Reminders` and `Files` removals end to end.** Their file lists are derived from
  the tree and are exact; the compile-and-fix loop for them is not measured. Expect more rounds than
  three, and expect the test list in [What else fails](#what-else-fails) to be a floor rather than a
  ceiling.

## The dependency you need to know about first

`Reminders` is not independent of `TodoLists`. A reminder is scheduled against a to-do item, and
that shows up as real code, not just a shared theme:

- `CancelRemindersOnTodoItemCompletedConsumer` (in `Reminders`) consumes
  `TodoItemCompletedDomainEvent`, a type owned by `TodoLists`.
- `ScheduleReminderUseCase` calls `ITodoListQueries.GetDetailAsync` to confirm the item it is
  scheduling against is real and belongs to the caller before it lets `Reminder.Schedule` run.
- `ReminderTargetQueries` (the persistence adapter behind `IReminderTargetQueries`) queries
  `context.TodoLists.SelectMany(list => list.Items)` to read completion state.

`Files` depends on neither, and neither depends on `Files`. The only arrow between them is a doc
comment, and it points from `Files` to `TodoLists`.

**Consequence:** `Reminders` and `Files` each come out on their own. `TodoLists` does not —
`Reminders` will not compile without it — so removing `TodoLists` means removing `Reminders` too,
whether or not you wanted to keep it.

**Three `<see cref>` doc comments cross features, and a cref is code here.**
`GenerateDocumentationFile` plus `TreatWarningsAsErrors` make a cref that no longer resolves a
`CS1574` error, not a warning. They are:

| The comment | Names | Breaks when you remove |
|---|---|---|
| `Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Features/TodoLists/Tracking/TodoListTracker.cs` | `ReminderTracker` | `Reminders` |
| `Src/Presentation/AppTemplate.Api/Features/TodoLists/Mapping/TodoListResponseMapping.cs` | `ReminderResponseMapping` | `Reminders` |
| `Src/Presentation/AppTemplate.Api/Features/Files/Mapping/StoredFileResponseMapping.cs` | `TodoListResponseMapping` | `TodoLists` |

Each makes a point that survives its example: reword the sentence, do not just delete the tag. The
first contrasts an aggregate with child rows against one without; the second and third cite a
mapping that answers with a string status rather than an enum, and any surviving mapper that does
the same will do.

## What to delete

By layer, mirroring `Src/`'s own structure. Each of these is a whole feature folder; delete the
directory, not file-by-file.

| Layer | TodoLists | Reminders | Files |
|---|---|---|---|
| Domain | `Src/Domain/AppTemplate.Domain/Features/TodoLists/` | `Src/Domain/AppTemplate.Domain/Features/Reminders/` | `Src/Domain/AppTemplate.Domain/Features/Files/` |
| Application | `Src/Application/AppTemplate.Application/Features/TodoLists/` | `Src/Application/AppTemplate.Application/Features/Reminders/` | `Src/Application/AppTemplate.Application/Features/Files/` |
| Persistence | `Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Features/TodoLists/` | `Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Features/Reminders/` | `Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Features/Files/` |
| API | `Src/Presentation/AppTemplate.Api/Features/TodoLists/` | `Src/Presentation/AppTemplate.Api/Features/Reminders/` | `Src/Presentation/AppTemplate.Api/Features/Files/` |
| Worker | — | `Src/Presentation/AppTemplate.Worker/Features/Reminders/` | `Src/Presentation/AppTemplate.Worker/Features/Files/` |
| In-memory doubles | — | `Src/Infrastructure/AppTemplate.Infrastructure.InMemory/Features/Reminders/` | `Src/Infrastructure/AppTemplate.Infrastructure.InMemory/Features/Files/` |
| Its own module | — | `Src/Infrastructure/AppTemplate.Infrastructure.Email/Features/` | `Src/Infrastructure/AppTemplate.Infrastructure.Storage/` |

Two entries in that last row deserve a word. `Reminders`' half of the email module is one file,
`EmailReminderNotifier.cs`, and it is the module's only feature — so its whole `Features/` directory
goes, and the module stays for `IEmailSender`. `Files`' half is a whole project,
`AppTemplate.Infrastructure.Storage`: the port shape is base and the `StoredFile` aggregate is
example, so a project that keeps any file storage at all keeps this project and re-points it.

And their test mirrors — same shape, under `Tests/` instead of `Src/`:

| Project | TodoLists | Reminders | Files |
|---|---|---|---|
| `Tests/Domain/AppTemplate.Domain.UnitTests/Features/` | yes | yes | yes |
| `Tests/Application/AppTemplate.Application.UnitTests/Features/` | yes | yes | yes |
| `Tests/Infrastructure/AppTemplate.Infrastructure.Persistence.UnitTests/Features/` | yes | yes | yes |
| `Tests/Presentation/AppTemplate.Api.UnitTests/Features/` | yes | yes | yes |
| `Tests/Presentation/AppTemplate.Worker.UnitTests/Features/` | — | yes | yes |
| `Tests/Infrastructure/AppTemplate.Infrastructure.InMemory.UnitTests/Features/` | — | — | yes |
| `Tests/Integration/AppTemplate.Api.IntegrationTests/` | `TodoLists/` | `Reminders/` | `Files/` and `Storage/` |

Plus, outside a `Features/` folder:

- `Tests/Application/AppTemplate.Application.UnitTests/TestDoubles/ATodoList.cs` (TodoLists) and
  `Tests/Application/AppTemplate.Application.UnitTests/TestDoubles/AReminder.cs` (Reminders) — test
  doubles that build a real aggregate, so nothing is left for them to build.
- `Tests/Infrastructure/AppTemplate.Infrastructure.Storage.UnitTests/` — the whole project, with
  `Files`.

One source file is easy to miss because it sits in the persistence layer without touching the
database: `ReminderDiagnostics`, the adapter behind `IReminderDiagnostics`, an OpenTelemetry counter
filed under
`Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Features/Reminders/Observability/`. It
goes with the rest of that folder.

After deleting a feature's directories, **delete the parent directory too if it is now empty.** The
one directory that empties is `Src/Infrastructure/AppTemplate.Infrastructure.Email/Features/`, and
it empties on the `Reminders` removal alone. `Features/` under the domain, the application,
persistence, the API and their test mirrors still holds whatever you kept.
`LayoutConventionTests.NoFolderInTheSourceTree_IsEmpty` names any that were missed.

**Two things the compiler reports in ways that are easy to misread.** An unused `using` is
`IDE0005`, which `TreatWarningsAsErrors` turns into an error — expect them in
`PersistenceModule.cs`, `AppDbContext.cs`, `EmailModule.cs`, `InMemoryModule.cs`, `ApiFactory.cs`
and `IntegrationTestBase.cs`, not only in `ApplicationModule.cs`. Remove them one at a time and
rebuild: `…Persistence.Common.Tracking` looks like it belongs to the deleted mappers but also holds
`StoredStamps`, which stays. And a `<see cref>` naming a deleted type is a build error too, as the
table above says — `InMemoryModule.cs`'s class summary names `IReminderNotifier`, so its comment has
to change along with its code.

## What to edit

These are the files outside a feature folder that name an example directly, in the order the build
reaches them. Each entry names the exact change. `(R)`, `(T)` and `(F)` mark which removal an entry
belongs to.

**`Src/Application/AppTemplate.Application/ApplicationModule.cs`**
Remove `AddScoped<ITodoListService, TodoListService>()` **(T)**, `AddScoped<IReminderService,
ReminderService>()` **(R)** and `AddScoped<IStoredFileService, StoredFileService>()` **(F)**. Remove
the domain-event consumer registrations that go with each: both
`AddDomainEventConsumer<TodoItemCompletedDomainEvent, …>()` calls with `TodoLists` **(T)**, the
`CancelRemindersOnTodoItemCompletedConsumer` one on its own with `Reminders` **(R)**, and the
`StoredFileDeletedDomainEvent` one with `Files` **(F)**.
`AddValidatorsFromAssemblyContaining<CreateTodoListCommandValidator>()` **(T)** needs a different
anchor type from the same assembly; any validator that survives works, `LoginCommandValidator` for
instance. Prune the now-unused `using` directives.

**`Src/Infrastructure/AppTemplate.Infrastructure.Persistence/PersistenceModule.cs`**
Delete the `AddTodoListsFeature` **(T)**, `AddRemindersFeature` **(R)** and `AddFilesFeature`
**(F)** private methods in full, and their calls inside `AddPersistenceModule`. Two comments in this
file refer across those methods and stop describing anything once their neighbour goes:
`AddFilesFeature`'s opens by pointing back at the features above it, and `AddRemindersFeature`'s
points at `AddTodoListsFeature` by name. Whichever survives, reword its comment to stand alone — *a
factory over one scoped instance rather than three registrations, so the repository, the flush
interceptor and the dispatch interceptor all resolve the same tracker.*

**`Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Common/Contexts/AppDbContext.cs`**
Remove the `DbSet<…>` property, the schema constant and the `builder.ApplyConfiguration(…)` calls
for each feature you removed: `TodoLists`/`TodoSchema`/three configurations **(T)**,
`Reminders`/`RemindersSchema`/one **(R)**, `StoredFiles`/`FilesSchema`/one **(F)**. The
`PlatformSchema` constant's doc comment cross-references `TodoSchema` in a `<see cref>` — fix or
drop that reference too **(T)**, or the build fails on a broken XML doc comment.

**`Src/Infrastructure/AppTemplate.Infrastructure.Email/EmailModule.cs`** **(R)**
Remove the `AddScoped<IReminderNotifier, EmailReminderNotifier>()` line and its two `using`
directives. Leave `AddScoped<IEmailSender, MailKitEmailSender>()`: `Auth`'s registration,
password-reset, email-change and resend-confirmation use cases all take `IEmailSender`, so this
module stays composed by every host.

**`Src/Infrastructure/AppTemplate.Infrastructure.InMemory/InMemoryModule.cs`**
Remove `AddInMemoryReminderNotifications` and its call inside `AddInMemoryModule` **(R)**, and
`AddInMemoryFileContent` and its call **(F)**. The class summary for `AddInMemoryModule` enumerates
the doubles it installs and names `IReminderNotifier` in a `<see cref>`; rewrite the sentence around
whatever is left. Leave the clock and email-sender substitutions alone.

**`Src/Presentation/AppTemplate.Api/Program.cs`** **(F)**
Remove `AddStorageModule(builder.Configuration)` and its `using
AppTemplate.Infrastructure.Storage;`. Nothing else in this file names a feature: `TodoLists` and
`Reminders` cascade automatically through `AddApplicationLayer` and `AddPersistenceModule`.

**`Src/Presentation/AppTemplate.Worker/Program.cs`**
Remove the `AddOptions<ReminderWorkerOptions>()` block, its
`AddSingleton<IValidateOptions<ReminderWorkerOptions>, …>()` line and
`AddHostedService<ReminderBackgroundService>()` **(R)**; the same three for `FileWorkerOptions` and
`FileBackgroundService`, plus `AddStorageModule` **(F)**. **Do not remove `AddEmailModule` with
`Reminders`.** `AddApplicationLayer` registers every `Auth` use case in this host too, four of them
take `IEmailSender`, and `ValidateOnBuild` requires every port the layer declares to resolve in
every host — so dropping the module leaves a build that is perfectly green and a Worker that will
not start. The two composition comments at the top of the file argue from the reminder loop; rewrite
them around the `Auth` use cases, which is the reason that survives.

**`Src/Presentation/AppTemplate.Worker/Common/Observability/WorkerObservabilityExtensions.cs`**
This is four separate edits, not one, and three of them are compile errors. With `Reminders`
**(R)**: drop `using AppTemplate.Worker.Features.Reminders;`, the
`.AddSource(ReminderInstruments.Name)` line and the `.AddMeter(ReminderInstruments.Name)` line — all
three name the Worker's own `ReminderInstruments`, which went with `Features/Reminders/`. Then drop
`.AddMeter("AppTemplate.Reminders")` and its comment: that string names the *persistence* project's
`ReminderDiagnostics` meter by a literal rather than a shared constant, so nothing tells you it now
names a meter that does not exist. With `Files` **(F)**: the same first three, for
`FileInstruments`. Update the comment above each group, which counts the loops.

**`Src/Presentation/AppTemplate.Worker/AppTemplate.Worker.csproj`** and
**`Src/Presentation/AppTemplate.Api/AppTemplate.Api.csproj`** **(F)**
Remove the `<ProjectReference>` to `AppTemplate.Infrastructure.Storage.csproj` from both. The Worker
keeps its reference to `AppTemplate.Infrastructure.Email.csproj` for the reason above; its comment
block explains both the email and the identity references in terms of the reminder loop, so it needs
rewriting around `IEmailSender` and `IRefreshTokenMaintenance` **(R)**. `ModuleDependencyTests`
reads the project-file graph, not just what the code calls, so a reference nobody uses is still a
declared arrow.

**`Src/Presentation/AppTemplate.Worker/Dockerfile`** and
**`Src/Presentation/AppTemplate.Api/Dockerfile`** **(F)**
Remove the `COPY` line for `AppTemplate.Infrastructure.Storage.csproj` in each, mirroring the
project reference above. **This is the one a build can hide from you.** `dotnet restore` does not
fail when a project file it expected is missing from the build context — it logs "Skipping project …
because it was not found" and the failure surfaces later, at `dotnet publish`, on a missing assets
file. Remove a `ProjectReference` and forget the `COPY`, and `dotnet build` and even a plain `dotnet
restore` look fine; only the image build breaks, several minutes in, on the publish step.

**`AppTemplate.sln`** **(F)**
Remove the `AppTemplate.Infrastructure.Storage` and `AppTemplate.Infrastructure.Storage.UnitTests`
project entries and their configuration blocks. `.template.config/template.json` lists no projects —
its `sources` key is an exclusion list — so there is nothing to change there for any removal.

**Test projects that reference what you removed** **(F)**
`Tests/Integration/AppTemplate.Api.IntegrationTests/AppTemplate.Api.IntegrationTests.csproj` and
`Tests/Architecture/AppTemplate.Architecture.Tests/AppTemplate.Architecture.Tests.csproj` both
reference `AppTemplate.Infrastructure.Storage`; the architecture project also anchors on
`StorageModule` in
`Tests/Architecture/AppTemplate.Architecture.Tests/Fixtures/ArchitectureAssemblies.cs` and composes
it in `Tests/Architecture/AppTemplate.Architecture.Tests/Composition/HostComposition.cs`.

## The migrations

The template ships **three** migrations, and which ones you touch depends on what you removed:

| Migration | Creates |
|---|---|
| `20260809002532_InitialCreate` | `identity` and `platform` — the schema every derived project keeps |
| `20260809002559_AddExampleFeatures` | `todo` **and** `reminders`, in one migration |
| `20260809211043_AddFiles` | `files` |

Nothing outside the examples references any of the three example schemas, so on a project that has
not yet applied a migration to a real database this is not a matter of generating a migration to
undo them. It is a matter of editing the files that create them, and then bringing
`AppDbContextModelSnapshot.cs` back into agreement with the code.

**Removing all three examples.** Delete both `*_AddExampleFeatures.*` and both `*_AddFiles.*` file
pairs. What survives is exactly `InitialCreate`, so — and only in this case — copying the
`BuildTargetModel` body of `20260809002532_InitialCreate.Designer.cs` over
`AppDbContextModelSnapshot.cs`'s `BuildModel` body is correct: that designer describes `identity`
and `platform` and nothing else. The two files differ only in the wrapping — the snapshot's class
extends `ModelSnapshot` and carries no `[Migration(...)]` attribute — so the method name is the only
edit the copy needs.

**Removing some but not all.** Do **not** copy an earlier designer over the snapshot. Subtract from
the current one instead. Every model description is a list of `modelBuilder.Entity("…")` blocks;
take out the blocks for the entities you removed, in all three passes of the file — the entity
definitions, the relationship blocks and the navigation blocks — and leave everything else alone.
The blocks are named by their persistence model's full type name, so
`…Features.TodoLists.Models.TodoListRecord` and its two siblings go with `TodoLists`,
`…Features.Reminders.Models.ReminderRecord` with `Reminders`, and
`…Features.Files.Models.StoredFileRecord` with `Files`. Make the same subtraction in the designer
file of every migration *later* than the one that created the entity, so the chain stays coherent:
`20260809211043_AddFiles.Designer.cs` describes the to-do list and reminder entities too, because it
was generated when they existed.

Copying `InitialCreate`'s designer over the snapshot in this case removes `StoredFiles` from the
model while the `AddFiles` migration and all the `Files` code are still there, and the next `dotnet
ef migrations add` then emits a duplicate `CreateTable` on `files`.

**Removing `Reminders` alone** additionally means editing `20260809002559_AddExampleFeatures.cs`
itself, since one migration creates both example schemas: drop the `EnsureSchema(name: "reminders")`
call, the `CreateTable` for `Reminders`, its two `CreateIndex` calls and the matching `DropTable` in
`Down`. Removing `TodoLists` means removing `Reminders` too, so in that direction the whole file
goes.

**Removing `Files` alone** is the cleanest of the three: `AddFiles` is the last migration, so
deleting both its files and subtracting `StoredFileRecord` from the snapshot is the whole edit. No
earlier designer mentions it.

`Tests/Infrastructure/AppTemplate.Infrastructure.Persistence.UnitTests/Migrations/PendingModelChangesTests.cs`
is what proves the edit is complete. It calls `Database.HasPendingModelChanges()`, needs no
database, and fails the moment the snapshot, the remaining migrations and the code's model disagree.
Run it before trusting this step done — it is the only thing here that will catch a subtraction that
took one block too many or too few.

**If a database has already had a migration applied to it**, deleting the file does not drop those
schemas there; it only stops a fresh database from ever creating them. Dropping them from a database
that already has them needs a real migration, generated against the edited project and reviewed for
exactly the `DropTable`/`DropSchema` groups you expect and nothing touched on a surviving table.
Give it a name no existing migration already carries — `dotnet ef` refuses a duplicate — so
`DropExampleFeatures` rather than `InitialCreate`. The tool is pinned by
`.config/dotnet-tools.json`.

## Configuration, deployment and the sample requests

None of this breaks a build, and all of it goes stale silently.

- **`Src/Presentation/AppTemplate.Worker/appsettings.json`** and
  **`Src/Presentation/AppTemplate.Worker/appsettings.Development.json`**: the `ReminderWorker`
  section **(R)** and the `FileWorker` section **(F)**.
- **`Src/Presentation/AppTemplate.Api/appsettings.json`** and
  **`Src/Presentation/AppTemplate.Worker/appsettings.json`**: the `Storage` and `ContentInspection`
  sections **(F)**, in both hosts.
- **`docker-compose.yml`** **(F)**: the two `Storage__*` environment blocks — one per host — the
  `minio` and `minio-bucket` services, and the `minio-data` volume.
- **`deploy/kubernetes/configmap-worker.yaml`**: the `ReminderWorker__Interval` and
  `ReminderWorker__Enabled` keys and the comment block above them **(R)**; the `FileWorker__*` keys,
  the `Storage__*` keys and the `ContentInspection__*` keys **(F)**.
  **`deploy/kubernetes/configmap-api.yaml`**: the `Storage__*` and `ContentInspection__*` keys
  **(F)**. **`deploy/kubernetes/api-deployment.yaml`**: the two `Storage__*` `secretKeyRef` entries
  **(F)**. **`deploy/kubernetes/secret.example.yaml`**: `Storage__AccessKeyId` and
  `Storage__SecretAccessKey` **(F)**, and the comment naming `IReminderNotifier` **(R)**.
  **`deploy/kubernetes/worker-deployment.yaml`**: its header comment counts the loops and names
  `ReminderBackgroundService`, and its replica note argues from `Reminder.TryClaim` **(R)**.
- **`AppTemplate.Api.http`**: the numbered request blocks for the feature you removed. They are the
  file's whole point, so they are worth replacing with your own rather than only deleting.
- **`Tests/Architecture/AppTemplate.Architecture.Tests/Rules/ConfigurationSurfaceTests.cs`** names
  `ReminderWorker` twice in its own documentation as the example of a section **(R)**; point it at
  another section that still exists.

## Does the Worker still have a reason to exist?

Yes, and by more than a margin. It hosts three `BackgroundService`s — maintenance, reminders and
files — so removing any one example leaves two. `MaintenanceBackgroundService` runs two purges
(`PurgeExpiredIdempotencyKeys`, `PurgeExpiredRefreshTokens`) on a timer and depends on no example
feature at all, so even removing all three leaves the host doing real work.

What it composes does not shrink as much as it looks like it should. `AddApplicationLayer` registers
every use case in the assembly, and `ValidateOnBuild` then requires every port those use cases
declare to resolve *in this host too* — not only the ports its own loops reach. That is why
`AppTemplate.Infrastructure.Identity` and `AppTemplate.Infrastructure.Email` both stay composed
after `Reminders` goes: `Auth`'s use cases take `IUserProfilesService` and `IEmailSender`, and they
are registered here whether or not anything in this process calls them.
`AppTemplate.Infrastructure.Storage` is the one module that genuinely leaves, and only with `Files`.

Either way the host still proves the template's actual claim: the same application layer, answering
an HTTP request in one process and a background loop in another, with no use case and no domain type
touched to make it work in either.

## What stops being demonstrated

This is the part worth budgeting time for, and it has two answers rather than one, because `Files`
carries most of what the other two carry. Every item below compiles fine and fails a test loudly, by
design — these rules refuse to pass over an empty set rather than pass silently — but "fails loudly"
is not the same as "still demonstrated somewhere."

### With `TodoLists` and `Reminders` gone, `Files` kept

Four things lose their only example, and one of them is measured.

- **`ILeaderLease`, and with it the only reason the worker can run at more than one replica.** The
  port and its `PostgresLeaderLease` adapter are base, not example — but its only consumer is
  `FireDueRemindersUseCase`. The two file loops document at length why they take *no* lease, so they
  are not a substitute. Removing `Reminders` alone is enough:
  `PortConventionTests.EveryApplicationPort_HasAConsumerInTheApplicationLayer` reports
  `ILeaderLease` as the one unconsumed port, and
  `BackgroundWorkTests.TheLeaderLease_IsTakenByAUseCase` finds no use case taking it. Both were
  observed. Two honest ways out, and the choice is about your project rather than about the
  template. Either put the first operation of yours that must not run twice at once under the lease
  — which is what it is for — or, if you genuinely have no such operation, delete the port, its
  adapter, its integration tests, the `Leases` entry in `LayoutConventionTests`'s `Common/`
  vocabulary and `BackgroundWorkTests` itself, and remember that
  `deploy/kubernetes/worker-deployment.yaml` may then no longer be raised above `replicas: 1`.
  Measured, that deletion costs four more compile errors: three `<see cref="ILeaderLease"/>`
  comments in the surviving `Files` code and one substitute registration in
  `ApplicationModuleTests`.
- **One event reaching two independent consumers.** `TodoItemCompletedDomainEvent` is consumed by
  `LogTodoItemCompletedConsumer` and by `CancelRemindersOnTodoItemCompletedConsumer`, registered by
  a different feature and unaware of each other. `Files` raises three domain events and has one
  consumer, for one of them — so domain events survive, and the cross-feature fan-out does not.
- **The conditional-GET round trip.** `TodoLists`' detail endpoint is the only one that publishes an
  `ETag` a client revalidates with `If-None-Match` for a `304`, which is what
  `Tests/Integration/AppTemplate.Api.IntegrationTests/Caching/CacheHeaderTests.cs` exercises. The
  `If-Match` write side survives: `FilesController` reads preconditions on two actions through the
  same `ApiControllerBase.ReadPrecondition`, and `Versioned<StoredFileDto>` crosses
  `ConfirmFileUploadUseCase`, so `412`/`428` and `Versioned<T>` stay demonstrated.
- **An aggregate with child entities.** `TodoList` owns `TodoItem`, which owns `Tag`, and the
  tracker's flush enrols a root whose own columns did not move so the root's `xmin` arbitrates the
  whole aggregate. `Reminder` and `StoredFile` are both flat, so what remains proves the simpler
  half only.

Everything else keeps a live example, because `Files` is one:

| Mechanism | Where it stays demonstrated |
|---|---|
| `IUnitOfWork` | six files under `Src/Application/AppTemplate.Application/Features/Files/` |
| The aggregate tracker / identity map, registered under three contracts | `Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Features/Files/Tracking/StoredFileTracker.cs` |
| The default-deny fallback authorisation policy | `Src/Presentation/AppTemplate.Api/Features/Files/Controllers/FilesController.cs` carries no `[Authorize]` and no `[AllowAnonymous]`, on the class or on any action |
| Domain events at all | three under `Src/Domain/AppTemplate.Domain/Features/Files/Events/`, one with a consumer |
| `ICollectionPolicy` | `Src/Application/AppTemplate.Application/Features/Files/Policies/StoredFileCollectionPolicy.cs` |
| Ownership isolation for a resource addressed by id | five `{fileId:guid}` routes on `FilesController` |
| `[Idempotent]` | `FilesController`'s registration action; the count drops from four to three |

Three of those need their test repointed rather than kept as is.
`Tests/Integration/AppTemplate.Api.IntegrationTests/Security/DefaultDenyAuthorizationTests.cs`
enumerates every verb on `TodoListsController` by hand and asserts the enumeration is complete;
repoint it at `FilesController`, which relies on the fallback the same way.
`Tests/Integration/AppTemplate.Api.IntegrationTests/Security/OwnershipIsolationTests.cs` and
`Tests/Integration/AppTemplate.Api.IntegrationTests/Idempotency/IdempotencyTests.cs` both drive
`TodoLists` over real HTTP; both have a `Files` equivalent to be rewritten against.

### With `Files` removed as well

Everything in that table loses its last example. The rules that catch it, each of which fails rather
than passing over nothing:

- `PortConventionTests.EveryApplicationPort_HasAConsumerInTheApplicationLayer` — `IUnitOfWork` joins
  `ILeaderLease` with zero callers in the application layer.
- `SharedInstanceRegistrationTests.EveryAggregateTracker_ResolvesAsOneInstanceUnderEveryContractItServes`
  — its floor is two trackers; the composed container has none.
- `DefaultDenyAuthorizationTests` — no controller left that relies on the fallback policy.
  `AuthController` decorates every action explicitly, and `MaintenanceController` and
  `AccountAdministrationController` each declare their own policy at the controller level.
- `DomainModelTests` — `AppTemplate.Domain` declares no aggregate, no entity, no value object and no
  domain event; `Features/` is empty and `Common/` holds only the primitives a real feature builds
  on. Five rules there exist to prove properties of a concrete domain model and have none to check.
  `Auth` never raises a domain event.
- `CollectionContractTests` — no collection policy registered anywhere, so its two rules about a
  policy's internal consistency are vacuous.
- `IdempotentActionsAreAlwaysPostTests` — no `[Idempotent]` action anywhere. The mechanism
  (`IdempotencyFilter`, `IIdempotencyStore`, the claim/complete/release state machine) is untouched
  and still unit-tested directly; what goes is the end-to-end proof.
- 404-not-403 for another user's resource — the deliberate choice that a 403 would leak that the id
  exists — has no example left, because no action in the remaining API is addressed by an `{id}`
  route segment at all. Every authentication action addresses either nobody or the caller identified
  by their own token; every maintenance action addresses everybody.
- `LayoutConventionTests.EveryInfrastructureModuleOnDisk_HasAVocabularyOfItsOwn` — its floor is five
  infrastructure modules and four remain, so the floor and the `Storage` entries in both
  vocabularies come out together.
- `StorageVocabularyTests` — four rules whose whole subject is the two-store shape.

None of this is a defect in the removal. It is what "the examples teach the architecture rather than
decorate it" costs once the lesson is taken away. For each affected test, do one of three things:
**skip** the ones asserting a property of a domain model or a registration that no longer exists,
with a comment naming exactly what brings it back — a real feature's first aggregate, its first
collection endpoint, its first version-conditioned mutation; **retarget** the ones that only ever
needed "some authenticated endpoint" at `GET /api/v1/auth/me` (reads) or `POST
/api/v1/auth/logout-all` (writes with no body); **delete** the ones with no substitute at all.

## What else fails

Beyond the mechanisms above, a removal breaks a tail of smaller things, by count or by name rather
than by missing mechanism. The `Reminders`-only list is measured — these are the nine tests that go
red, and the edit each one needs:

| Test | Why | The edit |
|---|---|---|
| `PendingModelChangesTests.TheModel_IsFullyCoveredByTheMigrations` | snapshot and model disagree | the migration section above |
| `PortConventionTests.EveryApplicationPort_HasAConsumerInTheApplicationLayer` | `ILeaderLease` unconsumed | the choice described above |
| `BackgroundWorkTests.TheLeaderLease_IsTakenByAUseCase` | same | same |
| `BackgroundWorkTests.NoBackgroundService_TakesTheLeaderLease` | its floor is three background services, two remain | lower the floor |
| `ObservabilityRegistrationTests.EveryDiagnosticsNameAHostDeclares_IsRegisteredByThatHost` | its floor is six instruments, four remain | lower the floor |
| `PersistenceModelTests.EveryEntityTypeConfiguration_IsAppliedByTheContext` and `…EveryConfigurationTheContextApplies_IsDeclaredInTheModule` | nothing, as long as the configuration file and its `ApplyConfiguration` call go together | delete both, or neither |
| `LayoutConventionTests.EveryFeatureFolder_IsNamedFromItsLayersVocabulary` | it requires every listed project to have a `Features/` folder, and the email module's is gone | see below |
| `DomainEventTests.NoEventIsListedAsUnconsumed_WhileSomethingConsumesIt` | `ReminderFiredDomainEvent` is still listed as deliberately unconsumed | drop the entry |

The layout one is worth a paragraph, because the obvious fix trades one red test for another.
`LayoutConventionTests` holds two hand-maintained vocabularies and asserts `checkedLayers` equals
`_vocabulary.Count` — every listed project must have a `Features/` folder — while
`EveryInfrastructureModuleOnDisk_HasAVocabularyOfItsOwn` asserts the converse, that every module on
disk is listed in both. Deleting the email module's entry satisfies the first and breaks the second.
Keep the entry and turn the identity assertion into a floor instead, naming the module that is
listed without a `Features/` folder and why. That was measured: both rules then pass.

Two more counts sit outside the architecture project and are not assertions you can lower blindly —
read the current value, recompute it after the removal, and update the comment beside it with the
number:

- `Tests/Application/AppTemplate.Application.UnitTests/ApplicationModuleTests.cs` holds
  `_knownUseCaseCount`, and a doc comment that breaks the total down per vertical. Removing
  `Reminders` takes it from 55 to 50; the comment has to lose the same clause. The number alone,
  without the comment saying what it now counts, is exactly the kind of assertion this repository's
  conventions warn against.
- `Tests/Integration/AppTemplate.Api.IntegrationTests/Security/IdempotentActionsAreAlwaysPostTests.cs`
  holds both a controller list and a count of `[Idempotent]` actions, which is four today.
- `Tests/Architecture/AppTemplate.Architecture.Tests/Rules/AdapterVisibilityTests.cs` holds a
  non-vacuity floor of ten adapters and a message enumerating them. Removing `Reminders` alone
  leaves it satisfied, so it needs no edit there; a wider removal does. Re-derive the enumeration
  from what survives — including the `Storage` adapters, if `Files` stays — rather than subtracting
  from the sentence.

And the fixture and helper code, which the compiler finds for you:

- `Tests/Application/AppTemplate.Application.UnitTests/ApplicationModuleTests.cs`'s provider fixture
  registers a substitute for every port a use case takes; drop the ones whose port is gone.
- `Tests/Integration/AppTemplate.Api.IntegrationTests/Infrastructure/TestDatabase.cs` names every
  schema `AppDbContext` declares, in the list it truncates between tests. Remove only the schemas
  you actually removed — a schema missing from that list is never reset, and its rows leak from one
  test into the next as an order-dependent intermittent, which is the worst category to diagnose.
  `Tests/Integration/AppTemplate.Api.IntegrationTests/Health/HealthEndpointTests.cs` asserts on two
  of the same constants for its "did the schemas migrate" check.
- `Tests/Application/AppTemplate.Application.UnitTests/Common/Concurrency/VersionPreconditionTests.cs`
  keeps its first half — the precondition object's own logic needs no aggregate — and loses the
  theory over the to-do list's mutating use cases **(T)**.
- `Tests/Domain/AppTemplate.Domain.UnitTests/Common/Abstractions/AuditableTests.cs` exercises
  `IAuditable` through `TodoList` **(T)**; rewrite it against a private nested test-only aggregate
  implementing the interface the same way every real one does — public getters, explicit interface
  setters.
- `Tests/Infrastructure/AppTemplate.Infrastructure.Persistence.UnitTests/Common/Saving/DomainEvents/DomainEventDispatcherTests.cs`
  and its `DomainEventDispatchSaveChangesInterceptorTests.cs` sibling raise real `TodoLists` events
  through a real tracker **(T)**; both the dispatcher and the interceptor are generic over
  `IDomainEvent`, so a private in-file event record and a minimal `IDomainEventSource` cover them
  with no feature at all.
- `Tests/Presentation/AppTemplate.Api.UnitTests/Conventions/ControllerContractTests.cs`'s
  deliberately-leaking test controller returns `TodoItemDto` **(T)**; repoint it at any application
  type from a vertical that survives.
- `Tests/Integration/AppTemplate.Api.IntegrationTests/Infrastructure/IntegrationTestBase.cs` holds a
  `TodoLists` route constant and its create/version/mutate helpers **(T)**. There is no generic
  replacement to leave behind in a shared base class for helpers that specifically create, version
  and conditionally mutate one aggregate.
- `Tests/Integration/AppTemplate.Api.IntegrationTests/Infrastructure/ApiFactory.cs` registers a
  *second* consumer of `TodoItemCompletedDomainEvent` purely to prove the dispatcher reaches every
  consumer of an event rather than only the first;
  `Tests/Integration/AppTemplate.Api.IntegrationTests/Infrastructure/RecordedDomainEvents.cs` is
  what it records into **(T)**. The property they prove stays covered at the unit level.
- Everything under `Tests/Integration/AppTemplate.Api.IntegrationTests/Security/`,
  `Tests/Integration/AppTemplate.Api.IntegrationTests/Caching/` and
  `Tests/Integration/AppTemplate.Api.IntegrationTests/Http/` that uses the to-do list route purely
  as "some authenticated endpoint" **(T)** takes the retarget above.
  `Tests/Integration/AppTemplate.Api.IntegrationTests/Http/RequestBodySizeLimitTests.cs` wants the
  anonymous `POST /api/v1/auth/register` instead, which needs no session set up first.
  `Tests/Integration/AppTemplate.Api.IntegrationTests/Security/FrameworkProblemDetailsTests.cs`'s
  "an authored error keeps its code" case needs a different status: no authored 404 exists outside
  the example features, so a 409 from registering the same address twice proves the same property.

## If you are keeping some and removing others

**Removing `Files`, keeping the rest.** Independent in both directions, and the only one whose
removal takes a whole infrastructure project, two `Dockerfile` lines, two solution entries and the
`minio` half of `docker-compose.yml` with it. Nothing outside `Files` names a `Files` type. Weigh it
against what the [table above](#with-todolists-and-reminders-gone-files-kept) says it is carrying:
if your project stores anything at all in an object store, re-pointing `StoredFile` at your own
metadata is less work than removing it and adding a second store back later.

**Removing `Reminders`, keeping `TodoLists` and `Files`.** The measured path, and the smallest one:
three rounds of `dotnet build` and nine failing tests, each with a one-line fix above. `TodoLists`
never references `Reminders` outside two doc comments. What actually leaves is `ILeaderLease`'s only
consumer, the second consumer of `TodoItemCompletedDomainEvent`, and one `[Idempotent]` action.

**Removing `TodoLists`, keeping `Reminders`.** Not a smaller version of the full removal — it does
not compile. `Reminders` calls `ITodoListQueries`, consumes `TodoItemCompletedDomainEvent` and
queries `context.TodoLists`. Either keep `TodoLists`, or accept that removing it means removing
`Reminders` too and rewrite `Reminders`' three touch points — `ScheduleReminderUseCase`,
`CancelRemindersOnTodoItemCompletedConsumer` and `ReminderTargetQueries` — against whatever replaces
the to-do item as the thing a reminder is scheduled against. That is no longer removing an example;
it is redesigning one.

## Verification

Run these yourself; they are the gates, in the order that fails fastest:

1. `dotnet build AppTemplate.sln` — 0 warnings, 0 errors. `TreatWarningsAsErrors` means an unused
   `using` and an unresolvable `<see cref>` both stop the build, so most of a removal's remaining
   work is visible here.
2. `dotnet test` on the ten unit and architecture test projects — no Docker needed. This is where a
   count, a non-vacuity floor or a hand-maintained list that no longer matches the tree turns red.
3. `PendingModelChangesTests` in particular, which also needs no database and is the only check on
   the migration edit.
4. `dotnet test` on `AppTemplate.Api.IntegrationTests` and
   `AppTemplate.Infrastructure.Identity.IntegrationTests`, which need Docker for their
   Testcontainers PostgreSQL. Nothing in this document has been confirmed against a running instance
   of either.
5. `docker build` on both `Dockerfile`s, for the `COPY` failure mode that `dotnet restore` hides.

If your removal produces something this document does not describe, the discovery-based architecture
tests — `RequireTypes`, the various `ShouldNotBeEmpty` and floor guards — are what will tell you
first, and loudly. That is what they are for.

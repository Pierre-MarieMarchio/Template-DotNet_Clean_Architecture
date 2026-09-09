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
its own metadata instead of deleting it. [`USING-THE-TEMPLATE.md`](USING-THE-TEMPLATE.md) says the
same thing from the other side. So
read the `Files` column as *what it costs to remove*, not as *what you are expected to do*.

[`ADDING-A-FEATURE.md`](ADDING-A-FEATURE.md) says a clean removal means rewriting the tests that use
an example as their subject, rather than only deleting files. This document is that rewrite, written
as a procedure.

**Read first, in this order**

- [What this document promises](#what-this-document-promises) — which removals were actually carried
  out, and which are reasoned
- [The dependency you need to know about first](#the-dependency-you-need-to-know-about-first) —
  `Reminders` does not compile without `TodoLists`
- [What stops being demonstrated](#what-stops-being-demonstrated) — the cost, before you pay it

**The procedure**

- [What to delete](#what-to-delete)
- [What to edit](#what-to-edit)
- [The migrations](#the-migrations)
- [Configuration, deployment and the sample requests](#configuration-deployment-and-the-sample-requests)
- [What else fails](#what-else-fails)
- [Verification](#verification)

**The two questions that come up**

- [Does the Worker still have a reason to exist?](#does-the-worker-still-have-a-reason-to-exist)
- [If you are keeping some and removing others](#if-you-are-keeping-some-and-removing-others)
- [Removing authentication](#removing-authentication)

## What this document promises

The `Reminders`-only removal below was carried out in full in a disposable copy of this repository:
it built after three rounds of `dotnet build` and ended with the fifteen unit projects and the one
architecture project green, `PendingModelChangesTests` included, which is what proves the migration
edit. Every file named in [What to edit](#what-to-edit) is a file some removal reaches.

**No test total is quoted, deliberately.** It was taken against a tree several projects smaller than
this one, and a number that cannot be reproduced is worse than no number — it reads as a prediction
and fails as one. What survives that re-shaping is the list of files and edits, which is the part to
rely on, and the round count, which is a floor rather than a forecast.

Three things are **not** measured here and you should treat them as such:

- **The integration suites.** `AppTemplate.Api.IntegrationTests` and
  `AppTemplate.Infrastructure.Auth.IntegrationTests` need Docker. Everything this document says
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

**In outline, removing a business feature is three things:** delete `Features/<F>/` in each layer
that has one, delete the feature's `AddX()` method in `ApplicationModule`, and delete the one line
in each `Program.cs` that calls it. Everything else below is a consequence — a schema in the
context, a migration, a worker loop, a configuration section, a test that used the feature as its
subject.

By layer, mirroring `Src/`'s own structure. Each of these is a whole feature folder; delete the
directory, not file-by-file.

**The boundary between what teaches and what stays is a project boundary, in the two inner layers,
which is what keeps this a list of directories rather than a list of edits.**
`AppTemplate.Domain.Core` holds the primitives and `AppTemplate.Application.Core` holds the
mechanisms — `Result`, `Error`, the `IUseCase` marker, validation, paging, idempotency, optimistic
concurrency, the cross-cutting ports — and neither project names an example, so nothing below asks
you to open one of them. Nor do the four written to the same grade further out:
`AppTemplate.Application.Auth` is authentication and has its own section below,
`AppTemplate.Presentation.Core` and `AppTemplate.Api.Core` are host and transport machinery, and
`AppTemplate.Infrastructure.Core` holds the mail template engine and the cache adapter. None of
the six names an example feature, which is what makes this a list of directories. `AppTemplate.Domain` and `AppTemplate.Application` hold the business, and
in this template that is `Features/` **plus one `Common/Tagging/` per layer**, which is the one
thing two features share *as business*: `TodoLists` and `Files` are both tagged, by the same rule.

So a removal touches a feature folder here, one of the composition files named in
[What to edit](#what-to-edit), and — for tagging alone — a shared folder whose fate depends on what
is left:

- **Removing `TodoLists`** leaves `Files` tagged, so `Common/Tagging/` stays in both layers. What
  goes with the feature is its own use of it: the three tag commands, their validators and their
  routes.
- **Removing `Files`** leaves `TodoLists` tagged, so `Common/Tagging/` stays here too. What goes
  with it is `ReplaceStoredFileTags`, the `PUT .../tags` route, and the `StoredFileTags` table —
  which means a migration, like every other table the feature owns.
- **Removing both** leaves nothing tagged. `Common/Tagging/` then has no consumer and should go
  with them, in both layers, along with its own tests and the two vocabulary entries in
  `LayoutConventionTests`.

**The one cached read in this template goes the same way.** Each of the two tagged features has a
`GET .../tags` route over a use case — `GetUsedTodoItemTags`, `GetUsedFileTags` — that reads through
`ICacheStore`, and every command changing an owner's tags drops the entry. `UsedTagsCache`, which
holds the key and the lifetime for both, sits in `Src/Application/AppTemplate.Application/Common/Tagging/`
and leaves with that folder. What does **not** leave is the mechanism: `ICacheStore` is a
cross-cutting port and `AddCacheStore()` is one line of each host's composition, so a tree with
every example removed keeps a working cache with nothing yet reading through it. Delete that line
only if you also mean to drop the capability.

If your own project has since added anything else to a `Features/`-adjacent `Common/`, check the
same way: whether what you are deleting was its only consumer.

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

`AppTemplate.Domain.Core.UnitTests` and `AppTemplate.Application.Core.UnitTests` are absent from
that table on purpose, and it is worth checking rather than assuming: every test in either is a
statement about a primitive, a result, a page request, a cursor or a precondition, with no
aggregate in sight, so no removal reaches them. `AppTemplate.Application.Auth.UnitTests` is absent
for a different reason: it mirrors a project that holds one feature, and that feature is not an
example — see [Removing authentication](#removing-authentication).

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

**In the application layer the whole edit is two deletions per feature**, because registration is
opt-in per feature rather than a scan of the assembly: the feature's method in `ApplicationModule`,
and the one line in each host that calls it. Nothing else claims to have registered the feature, so
there is no anchor type to re-point, no consumer list to prune elsewhere and no port that has to
stay resolvable in a host that no longer offers the feature.

**`Src/Application/AppTemplate.Application/ApplicationModule.cs`**
Delete the whole method for the feature you removed — `AddTodoLists` **(T)**, `AddReminders`
**(R)**, `AddFiles` **(F)** — including its `AddScoped<…Service, …>()` line, its
`AddDomainEventConsumer<…>()` call or calls, and its `private const string` vertical name. Prune the
now-unused `using` directives. The `AddFeature` helper and the two reflection helpers below it name
no feature and stay as they are; the class summary explains why there is no call that adds
everything, and that argument survives every removal unchanged.

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

**`Src/Presentation/AppTemplate.Api/Program.cs`**
Remove the feature's one composition line — `AddTodoLists()` **(T)**, `AddReminders()` **(R)**,
`AddFiles()` **(F)** — and, with `Files`, `AddStorageModule(builder.Configuration)` and its `using
AppTemplate.Infrastructure.Storage;`. The persistence side needs no line here: it cascades through
`AddPersistenceModule`.

**`Src/Presentation/AppTemplate.Worker/Program.cs`**
Remove the same composition line as in the API, then the `AddOptions<ReminderWorkerOptions>()`
block, its `AddSingleton<IValidateOptions<ReminderWorkerOptions>, …>()` line and
`AddHostedService<ReminderBackgroundService>()` **(R)**; the same three for `FileWorkerOptions` and
`FileBackgroundService`, plus `AddStorageModule` **(F)**. **Do not remove `AddEmailModule` or
`AddAuthModule` with `Reminders`.** This host also calls `AddAuthApplication()`, which registers
every use case `AppTemplate.Application.Auth` declares, and `ValidateOnBuild` then requires all
twenty of that project's ports to resolve here — four of the use cases take `IEmailSender` — so
dropping either module leaves a build that is perfectly green and a Worker that will not start. The
composition comment at the top of the file argues from the reminder loop *and* from
`AddAuthApplication`; the second half is the reason that survives a `Reminders` removal, so trim the
first half rather than the whole block. Dropping authentication itself is a different operation:
see [Removing authentication](#removing-authentication).

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

Each context ships **one** migration, and they are independent:

| Context | Migration | Creates |
|---|---|---|
| `AppDbContext` | `Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Migrations/` | `todo`, `reminders`, `files` and `platform` — seven tables |
| `AuthDbContext` | `Src/Infrastructure/AppTemplate.Infrastructure.Auth/Migrations/` | `identity` — nine tables plus the key ring |

**Removing an example touches the business migration and nothing else.** Authentication's is a
different file, in a different project, with a history table of its own, so no removal here can
reach it.

Nothing outside the examples references any of the three example schemas, so on a project that has
not yet applied a migration to a real database this is not a matter of generating a migration to
undo them.

**The simplest correct path is to regenerate.** The business half ships a single initial migration,
so there is no chain to keep coherent and no earlier designer to reconcile: delete both its files
and `AppDbContextModelSnapshot.cs`, then

```bash
dotnet ef migrations add InitialCreate \
  --project Src/Infrastructure/AppTemplate.Infrastructure.Persistence \
  --startup-project Src/Infrastructure/AppTemplate.Infrastructure.Persistence \
  --output-dir Migrations
```

against the edited model. What comes out creates exactly the schemas your remaining features need,
plus `platform` for the idempotency table that belongs to `AppTemplate.Application.Core`'s own
mechanism and that every derived project keeps.

**Do that only on a project no database has yet applied a migration to.** Otherwise the removal is a
real `DropTable`/`DropSchema` migration, generated against the deployed model and reviewed line by
line — regenerating an initial migration that a database has already recorded makes the tool and the
database disagree about what has been applied.

**If you would rather edit than regenerate**, subtract from both files by hand: in the migration,
drop the `EnsureSchema`, `CreateTable` and `CreateIndex` calls for what went and the matching
`DropTable` in `Down`; in the snapshot, take out the `modelBuilder.Entity("…")` blocks for the same
entities in all three passes of the file — the entity definitions, the relationship blocks and the
navigation blocks. The blocks are named by their persistence model's full type name, so
`…Features.TodoLists.Models.TodoListRecord` and its two siblings go with `TodoLists`,
`…Features.Reminders.Models.ReminderRecord` with `Reminders`, and
`…Features.Files.Models.StoredFileRecord` with `Files`.

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

What it composes shrinks by exactly the line you delete, and no further. Removing a business
feature removes that feature's `AddX()` call and nothing else from the graph. What does not shrink
is authentication: this host calls `AddAuthApplication()`, and `ValidateOnBuild` then requires all
twenty ports `AppTemplate.Application.Auth` declares to resolve *in this host too* — not only the
ports its own loops reach. That is why `AppTemplate.Infrastructure.Auth` and
`AppTemplate.Infrastructure.Email` both stay composed after `Reminders` goes: those use cases take
`IUserProfilesService` and `IEmailSender`, and they are registered here whether or not anything in
this process calls them. `AppTemplate.Infrastructure.Storage` is the one module that leaves with a
business feature, and only with `Files`.

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
- **An aggregate with child entities.** `TodoList` owns `TodoItem`, and the tracker's flush enrols
  a root whose own columns did not move so the root's `xmin` arbitrates the whole aggregate.
  `Reminder` and `StoredFile` are both flat, so what remains proves the simpler half only. Tagging
  is not the missing half: `StoredFile` carries a `TagSet` too, and its rows are reconciled the same
  way — what goes is the *nesting*, not the collection.

Everything else keeps a live example, because `Files` is one:

| Mechanism | Where it stays demonstrated |
|---|---|
| `IUnitOfWork` | six files under `Src/Application/AppTemplate.Application/Features/Files/` |
| The aggregate tracker / identity map, registered under three contracts | `Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Features/Files/Tracking/StoredFileTracker.cs` |
| The default-deny fallback authorisation policy | `Src/Presentation/AppTemplate.Api/Features/Files/Controllers/FilesController.cs` carries no `[Authorize]` and no `[AllowAnonymous]`, on the class or on any action |
| Domain events at all | three under `Src/Domain/AppTemplate.Domain/Features/Files/Events/`, one with a consumer |
| `ICollectionPolicy` | `Src/Application/AppTemplate.Application/Features/Files/Policies/StoredFileCollectionPolicy.cs` |
| Ownership isolation for a resource addressed by id | five `{fileId:guid}` routes, four on `FilesController` and the tag replacement on `FileTagsController` |
| `[Idempotent]` | `FilesController`'s registration action; the count drops from four to three |

Three of those need their test repointed rather than kept as is.
`Tests/Integration/AppTemplate.Api.IntegrationTests/Security/DefaultDenyAuthorizationTests.cs`
enumerates every verb on the three classes answering the `todo-lists` prefix -- `TodoListsController`,
`TodoItemsController` and `TodoItemTagsController` -- by hand, and asserts the enumeration covers all
sixteen actions across them; repoint it at `FilesController` and `FileTagsController`, which rely on
the fallback the same way.
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
  domain event: `Features/` is all that project holds, and it is empty. The primitives a real
  feature builds on are one project inwards, in `AppTemplate.Domain.Core`, and nothing about
  removing a feature touches them. Five rules there exist to prove properties of a concrete domain
  model and have none to check. `Auth` never raises a domain event.
- `CollectionContractTests` — no collection policy registered anywhere, so its two rules about a
  policy's internal consistency are vacuous.
- `IdempotentActionsAreAlwaysPostTests` — no `[Idempotent]` action anywhere. The mechanism
  (`IdempotencyFilter`, `IIdempotencyStore`, the claim/complete/release state machine) is untouched
  and still unit-tested directly; what goes is the end-to-end proof.
- 404-not-403 for another user's resource — the deliberate choice that a 403 would leak that the id
  exists — has no example left, because no action in the remaining API is addressed by an `{id}`
  route segment at all. Every authentication action addresses either nobody or the caller identified
  by their own token; every maintenance action addresses everybody.
- `LayoutConventionTests.EveryProjectOnDisk_HasAVocabularyOfItsOwn` — it reads every project under
  `Src/` off the disk, not the infrastructure modules alone, and its floor is twelve projects, which
  is exactly how many there are. So deleting `AppTemplate.Infrastructure.Storage` takes the
  `Storage` entries out of both vocabularies *and* drops the floor below what it asserts. Lowering
  it is a claim about how many projects your template has — recount the tree and write that number,
  do not subtract one from the old one.
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
`LayoutConventionTests` holds two hand-maintained vocabularies, and each of the two folder rules
reports a project listed with words whose folder is not on disk — while
`EveryProjectOnDisk_HasAVocabularyOfItsOwn` asserts the converse, that every project on disk is
listed in both. Deleting the email module's entry outright satisfies the first and breaks the
second. The fix is the third state the dictionaries carry: set the entry to `null`, which says this
project has no `Features/` folder at all, and is checked in that direction too — a `Features/`
reappearing there fails. That is the same entry `AppTemplate.Domain`, `AppTemplate.Application` and
`AppTemplate.Application.Auth` already carry for `Common/`: null means "no such folder today", not
"no such folder allowed". Those three would grow one the moment several of their features shared
something *as business* — a value object, a DTO, a policy that spans them — and the rule's failure
message says which of the two kinds to decide it is: something that knows no feature belongs in the
layer's `.Core` project, something several features share as business belongs in the business
project.

Two more counts sit outside the architecture project and are not assertions you can lower blindly —
read the current value, recompute it after the removal, and update the comment beside it with the
number:

- `Tests/Application/AppTemplate.Application.UnitTests/ApplicationModuleTests.cs` holds
  `_knownUseCaseCount`, and a doc comment that breaks the total down per vertical. Removing
  `Reminders` takes it from 29 to 24; the comment has to lose the same clause, and the feature's
  entry comes out of the `FeatureCalls` theory data beside it. The number alone, without the comment
  saying what it now counts, is exactly the kind of assertion this repository's conventions warn
  against. `Tests/Application/AppTemplate.Application.Auth.UnitTests/ApplicationAuthModuleTests.cs`
  is its counterpart and no business removal touches it.
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
- The version precondition needs no edit at all, and the reason is the project boundary. Its own
  logic is tested in
  `Tests/Application/AppTemplate.Application.Core.UnitTests/Common/Concurrency/VersionPreconditionTests.cs`,
  which names no aggregate and mentions no example; the theory over the to-do list's mutating use
  cases is `Tests/Application/AppTemplate.Application.UnitTests/Features/TodoLists/UseCases/TodoListVersionPreconditionTests.cs`
  and goes with the `Features/TodoLists/` mirror in the table above **(T)**. This is the shape most
  of the application layer's mechanisms now have: what a removal touches is under `Features/`, and
  what it must not touch is in another project.
- `Tests/Infrastructure/AppTemplate.Infrastructure.Persistence.UnitTests/Common/Saving/DomainEvents/DomainEventDispatchSaveChangesInterceptorTests.cs`
  raises real `TodoLists` events through a real tracker **(T)**; the interceptor is generic over
  `IDomainEvent`, so a private in-file event record and a minimal `IDomainEventSource` cover it with
  no feature at all. Its dispatcher sibling needs no edit: it lives in
  `Tests/Infrastructure/AppTemplate.Infrastructure.Core.UnitTests/Common/Saving/DomainEvents/DomainEventDispatcherTests.cs`
  and already raises an event of its own, because a mirror of a package-grade project may name no
  business type.
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

## Removing authentication

Authentication is not an example feature — it is the capability a derived project is most likely to
keep and the second most likely to replace wholesale with its own identity provider. It is also the
one removal that is **not** symmetrical with the three above, so it gets its own section.

The composable half is genuinely composable, and asserted rather than claimed —  but it is the
*application layer*, not the container.
`LayerDependencyTests.TheApplicationLayer_KnowsNothingOfAuthentication` reads the assembly manifests
and finds that neither the business features nor the mechanisms name anything in
`AppTemplate.Application.Auth`. A container of the business features with no `AddAuthApplication()`
and no identity module does **not** build, and
`ContainerCompositionTests.RemovingAuthentication_IsHeldUpByOneInfrastructureCoupling_NotByTheApplicationLayer`
is what says so and names why. So the first three steps are small: delete the
`AddAuthApplication()` line from both `Program.cs` files, delete
`Src/Application/AppTemplate.Application.Auth/` and `Src/Infrastructure/AppTemplate.Infrastructure.Auth/`
with their test mirrors and solution entries, and delete
`Src/Presentation/AppTemplate.Api/Features/Auth/` — `AuthController` and
`AccountAdministrationController`.

**Then four things answer back, and none of them is in either of those two projects.**

- **The email module will not compile without it.** `EmailReminderNotifier` resolves
  `IUserProfilesService` — an authentication port — to find the address a due reminder is rung at.
  That is `Src/Infrastructure/AppTemplate.Infrastructure.Email/Features/Reminders/EmailReminderNotifier.cs`,
  and it is why the module is absent from the container that test builds. Either the reminder loop
  learns an address some other way, or it goes.
- **The business context does not know authentication exists.**
  `Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Common/Contexts/AppDbContext.cs`
  derives from `DbContext` and maps the business tables and `IdempotencyKeys`.
  `AppTemplate.Infrastructure.Auth` owns `AuthDbContext`, the nine identity tables and the key ring,
  in the `identity` schema with a migrations history of its own. Deleting the project takes the model
  with it.
- **The seeder goes with its module.** `IIdentitySeeder` is registered by the authentication module,
  so nothing in the business half is left holding a dependency only that module could satisfy.
  `ContainerCompositionTests.RemovingAuthentication_IsHeldUpByOneInfrastructureCoupling_NotByTheApplicationLayer`
  asserts the *absence* of that coupling, so it cannot come back unnoticed.
- **The migrations are already separate**, so removing authentication is deleting its migration
  along with its project. The business migration is untouched, and `platform` — the idempotency
  table that belongs to `AppTemplate.Application.Core`'s own mechanism, which every derived project
  keeps — was never in the same file.

**What is left to answer for is one adapter.** `IReminderNotifier`'s only implementation is in the
email module, and that implementation resolves `IUserProfilesService` to find the address a due
reminder is rung at. Either drop the reminder feature with authentication, denormalise the address
onto the reminder, or write a notifier that gets it elsewhere. That is the whole of it now: a
project, a line in each host's composition, and that one door.

## Verification

Run these yourself; they are the gates, in the order that fails fastest:

1. `dotnet build AppTemplate.sln` — 0 warnings, 0 errors. `TreatWarningsAsErrors` means an unused
   `using` and an unresolvable `<see cref>` both stop the build, so most of a removal's remaining
   work is visible here.
2. `dotnet test` on the fifteen unit projects and the architecture one — sixteen of the eighteen
   under `Tests/`, and no Docker needed for any of them. This is where a count, a non-vacuity floor
   or a hand-maintained list that no longer matches the tree turns red.
3. `PendingModelChangesTests` in particular, which also needs no database and is the only check on
   the migration edit.
4. `dotnet test` on `AppTemplate.Api.IntegrationTests` and
   `AppTemplate.Infrastructure.Auth.IntegrationTests`, which need Docker for their
   Testcontainers PostgreSQL. Nothing in this document has been confirmed against a running instance
   of either.
5. `docker build` on both `Dockerfile`s, for the `COPY` failure mode that `dotnet restore` hides.

If your removal produces something this document does not describe, the discovery-based architecture
tests — `RequireTypes`, the various `ShouldNotBeEmpty` and floor guards — are what will tell you
first, and loudly. That is what they are for.

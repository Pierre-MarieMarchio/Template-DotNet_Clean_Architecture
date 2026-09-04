# Removing the example features

This template ships two worked examples: `TodoLists` and `Reminders`. They exist to teach —
an aggregate with real invariants, domain events, optimistic concurrency, a paginated
collection endpoint, a background worker calling the same use cases as the API — and a
derived project is expected to delete them once it has read what they show. `docs/ADDING-A-FEATURE.md`
promises this can be done "cleanly"; this document is that promise, walked and verified rather
than asserted. Every path below was confirmed to exist, every edit was actually made, and the
result was built and tested — twice: once with both examples removed, once checking what removing
only one of them would cost.

**The short version:** it compiles and the test suite can be made green again, but not by deleting
files alone. A determined but non-obvious set of cross-cutting mechanisms — optimistic concurrency,
the aggregate tracker / identity map, the default-deny fallback authorisation policy, the
domain-event dispatcher, `IUnitOfWork` itself — turn out to have been demonstrated **only** by these
two features. Removing both does not break them; it leaves them with nothing left in the repository
to exercise them. That is the part worth reading before you start, not after: see
[What has no replacement](#what-has-no-replacement) below.

## The dependency you need to know about first

`Reminders` is not independent of `TodoLists`. A reminder is scheduled against a to-do item, and
that shows up as real code, not just a shared theme:

- `CancelRemindersOnTodoItemCompletedConsumer` (in `Reminders`) consumes
  `TodoItemCompletedDomainEvent`, a type owned by `TodoLists`.
- `ScheduleReminderUseCase` calls `ITodoListQueries.GetDetailAsync` to confirm the item it is
  scheduling against is real and belongs to the caller before it lets `Reminder.Schedule` run.
- `ReminderTargets` (the persistence adapter behind `IReminderTargets`) queries
  `context.TodoLists.SelectMany(list => list.Items)` to read completion state.

**Consequence:** you can remove `Reminders` on its own — nothing in `TodoLists` references it back
except a doc-comment cross-reference in `TodoListTracker.cs`, which is prose, not code. You
**cannot** remove `TodoLists` on its own; `Reminders` will not compile without it. Removing
`TodoLists` alone means removing `Reminders` too, whether or not you wanted to keep it.

Read the two removals as one procedure with an optional second half: everything in
[What to delete](#what-to-delete) and [What to edit](#what-to-edit) marked **(Reminders only)** is
skippable if you are keeping `Reminders`; nothing marked **(TodoLists)** is.

## What to delete

By layer, mirroring `Src/`'s own structure. Each of these is a whole feature folder; delete the
directory, not file-by-file.

| Layer | TodoLists | Reminders |
|---|---|---|
| Domain | `Src/Domain/AppTemplate.Domain/Features/TodoLists/` | `Src/Domain/AppTemplate.Domain/Features/Reminders/` |
| Application | `Src/Application/AppTemplate.Application/Features/TodoLists/` | `Src/Application/AppTemplate.Application/Features/Reminders/` |
| Persistence | `Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Features/TodoLists/` | `Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Features/Reminders/` |
| API | `Src/Presentation/AppTemplate.Api/Features/TodoLists/` | `Src/Presentation/AppTemplate.Api/Features/Reminders/` |
| In-memory test doubles | — | `Src/Infrastructure/AppTemplate.Infrastructure.InMemory/Reminders/` |
| Worker | — | `Src/Presentation/AppTemplate.Worker/Features/Reminders/` |
| Email adapter | — | `Src/Infrastructure/AppTemplate.Infrastructure.Email/Services/EmailReminderNotifier.cs` (one file, not a folder) |

And their test mirrors — same shape, under `Tests/` instead of `Src/`:

- `Tests/Domain/AppTemplate.Domain.UnitTests/Features/{TodoLists,Reminders}/`
- `Tests/Application/AppTemplate.Application.UnitTests/Features/{TodoLists,Reminders}/`
- `Tests/Infrastructure/AppTemplate.Infrastructure.Persistence.UnitTests/Features/{TodoLists,Reminders}/`
- `Tests/Presentation/AppTemplate.Api.UnitTests/Features/{TodoLists,Reminders}/`
- `Tests/Integration/AppTemplate.Api.IntegrationTests/{TodoLists,Reminders}/`
- `Tests/Presentation/AppTemplate.Worker.UnitTests/Features/Reminders/` (Reminders only)

One more file that is easy to miss because it sits in the persistence layer without touching the
database — it is the adapter behind `IReminderDiagnostics`, an OpenTelemetry counter filed under
`Observability/` rather than among the feature's mappers and repositories:

- `Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Features/Reminders/Observability/ReminderDiagnostics.cs` (Reminders only)

After deleting a feature's directories, **delete the parent directory too if it is now empty.**
Removing both examples empties `Src/Domain/AppTemplate.Domain/Features/`,
`Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Features/Reminders/Observability/`, and the mirror
`Features` directories under the domain and persistence unit-test projects. An architecture test —
`LayoutConventionTests.NoFolderInTheSourceTree_IsEmpty` — exists specifically to catch a folder left
behind after everything inside it is gone, so it will name any that were missed.

**Two things the compiler reports in ways that are easy to misread.** An unused `using` is
`IDE0005`, which `TreatWarningsAsErrors` turns into an error — expect them in
`PersistenceModule.cs`, `AppDbContext.cs`, `EmailModule.cs`, `InMemoryModule.cs`, `ApiFactory.cs`
and `IntegrationTestBase.cs`, not only in `ServiceRegistration.cs`. Remove them one at a time and
rebuild: `…Persistence.Common.Mapping` looks like it belongs to the deleted mappers but also holds
`StoredStamps`, which stays. And a `<see cref="…"/>` naming a deleted type
is a build error too — `InMemoryModule.cs`'s class summary names `IReminderNotifier`, so its
comment has to change along with its code.

## What to edit

These are the files outside a feature folder that name `TodoLists` or `Reminders` directly. Each
entry names the exact change, not just the file.

**`Src/Application/AppTemplate.Application/ServiceRegistration.cs`**
Remove the `AddScoped<ITodoListAccess, TodoListAccess>()` and
`AddScoped<IReminderAccess, ReminderAccess>()` lines, and the two
`AddDomainEventConsumer<TodoItemCompletedDomainEvent, …>()` calls (`LogTodoItemCompletedConsumer`
and `CancelRemindersOnTodoItemCompletedConsumer`) — both event and both consumers live inside the
features you just deleted. `AddValidatorsFromAssemblyContaining<CreateTodoListCommandValidator>()`
needs a different anchor type from the same assembly; any validator that survives works, for
example `LoginCommandValidator`. Prune the now-unused `using` directives; the compiler will tell you
which ones.

**`Src/Infrastructure/AppTemplate.Infrastructure.Persistence/PersistenceModule.cs`**
Delete the `AddTodoListsFeature` and `AddRemindersFeature` private methods in full, and their two
calls inside `AddPersistenceModule`. Nothing else in this file names either feature.

**`Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Common/Contexts/AppDbContext.cs`**
Remove the `TodoLists` and `Reminders` `DbSet<…>` properties, the `TodoSchema` and `RemindersSchema`
constants, and the four `builder.ApplyConfiguration(…)` calls in `OnModelCreating` for
`TodoListRecordConfiguration`, `TodoItemRecordConfiguration`, `TodoItemTagRecordConfiguration` and
`ReminderRecordConfiguration`. The `PlatformSchema` constant's doc comment cross-references
`TodoSchema` in a `<see cref>` — fix or drop that reference too, or the build fails on a broken XML
doc comment (this project treats that as an error, not a warning).

**`Src/Infrastructure/AppTemplate.Infrastructure.Email/EmailModule.cs`**
Remove the `AddScoped<IReminderNotifier, EmailReminderNotifier>()` line and its `using`. Leave
`AddScoped<IEmailSender, MailKitEmailSender>()` — Auth's registration, password-reset and
email-change flows depend on it, so this module stays composed by every host regardless of which
example features survive.

**`Src/Infrastructure/AppTemplate.Infrastructure.InMemory/InMemoryModule.cs`**
Remove the `AddInMemoryReminderNotifications` method and the call to it inside `AddInMemoryModule`,
plus the `RemoveAll<IReminderNotifier>()`/`AddScoped<IReminderNotifier, …>()` pair. Leave the clock
and email-sender substitution alone.

**`Src/Presentation/AppTemplate.Api/Program.cs`**
Nothing to change here beyond what cascades automatically through `AddApplicationLayer` and
`AddPersistenceModule`. The file names no feature directly.

**`Src/Presentation/AppTemplate.Worker/Program.cs`** (Reminders only, but only relevant once you
have already decided to remove it)
Remove the `AddOptions<ReminderWorkerOptions>()` block, the
`AddSingleton<IValidateOptions<ReminderWorkerOptions>, …>()` line, and
`AddHostedService<ReminderBackgroundService>()`. Remove the `AddEmailModule(builder.Configuration)`
call and its `using AppTemplate.Infrastructure.Email;` — **this is the one that is easy to miss**,
because it looks unrelated to Reminders at a glance. It is not: the Worker composes the email
module for exactly one reason, stated in its own comment — `IReminderNotifier`'s adapter lives
there, and a reminder that comes due is rung by mail. With `Reminders` gone the Worker calls
`IEmailSender` from nowhere, and the module has nothing left to do in that process.

**`Src/Presentation/AppTemplate.Worker/Common/Observability/WorkerObservabilityExtensions.cs`**
Remove the `.AddMeter("AppTemplate.Reminders")` call (and its comment) from the metrics pipeline —
that string names `ReminderDiagnostics`'s meter, in a different project, by a literal rather than a
shared constant (the class is internal), so nothing else will tell you it is now naming a meter that
no longer exists.

**`Src/Presentation/AppTemplate.Worker/AppTemplate.Worker.csproj`**
Remove the `<ProjectReference>` to `AppTemplate.Infrastructure.Email.csproj`. Follows directly from
removing `AddEmailModule` above — a `ProjectReference` nobody uses any more is still a declared,
inward-pointing arrow, and `AppTemplate.Architecture.Tests` (`ModuleDependencyTests`) checks the
project file graph, not just what the code calls.

**`Src/Presentation/AppTemplate.Worker/Dockerfile`**
Remove the `COPY` line for `AppTemplate.Infrastructure.Email.csproj` that mirrors the project
reference above. **This is the one a build can hide from you.** `dotnet restore` does not fail when
a project file it expected is missing from the build context — it logs "Skipping project … because
it was not found" and the failure surfaces later, at `dotnet publish`, on a missing assets file. If
you remove the `ProjectReference` but forget this line, `dotnet build` and even a plain `dotnet
restore` on the csproj will look fine; only the Docker image build breaks, and it breaks on the
publish step, several minutes in.

## Does the Worker still have a reason to exist?

Yes — `MaintenanceBackgroundService` runs two purges (`PurgeExpiredIdempotencyKeys`,
`PurgeExpiredRefreshTokens`) on a timer, and neither depends on either example feature. Once
`Reminders` is gone the Worker composes `AppTemplate.Application`, `AppTemplate.Infrastructure.Persistence`
and `AppTemplate.Infrastructure.Identity` — the last one only because `IRefreshTokenMaintenance`'s
sole adapter lives there — and nothing else. It still proves the template's actual claim: the same
application layer, answering an HTTP request in one host and a background loop in another, with no
use case and no domain type touched to make it work in either.

## The migration

The template ships its schema as exactly two migrations, and the split exists for this moment.
`InitialCreate` is the schema every derived project keeps — `identity` and `platform` — and
`AddExampleFeatures` is `todo` and `reminders`, and nothing else. Neither the baseline nor any
surviving code references either example schema, so removing them from a project that has not yet
applied any migration to a real database is not a matter of generating a migration to undo them —
it is a matter of deleting the file that created them:

```bash
rm Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Migrations/*_AddExampleFeatures.cs \
   Src/Infrastructure/AppTemplate.Infrastructure.Persistence/Migrations/*_AddExampleFeatures.Designer.cs
```

That leaves `AppDbContextModelSnapshot.cs` describing a model — `TodoListRecord`, `TodoItemRecord`,
`TodoItemTagRecord`, `ReminderRecord`, their relationships and navigations — that no longer matches
either the one remaining migration or the code once the persistence models above are deleted too
(see [What to delete](#what-to-delete)). There is no new migration for `dotnet ef` to fold the
snapshot into here, so the snapshot has to be hand-edited back to describing exactly the migration
that is left: copy the `BuildTargetModel` body of the `InitialCreate` migration's designer file over
`AppDbContextModelSnapshot.cs`'s `BuildModel` body verbatim — same entities, described the same way —
and drop the four example entities' blocks along with the relationship/navigation blocks that name
them. The two files differ only in the wrapping: the snapshot's class extends `ModelSnapshot` and
carries no `[Migration(...)]` attribute, where the migration's carries one and no base class override
signature change beyond the method name (`BuildModel` vs. `BuildTargetModel`).
`Tests/Infrastructure/AppTemplate.Infrastructure.Persistence.UnitTests/Migrations/PendingModelChangesTests.cs`
(`Database.HasPendingModelChanges()`, no database required) is what proves the edit is complete — it
fails the moment the snapshot, the remaining migration and the code's model disagree with each
other, so run it before trusting this step done.

Once both examples are gone, `AppDbContext.TodoSchema` and `.RemindersSchema` go with them — grep for
either constant after editing `AppDbContext.cs`, because `TestDatabase.cs` and
`HealthEndpointTests.cs` in the integration suite both name `AppDbContext.TodoSchema` directly, for
the per-test truncation statement and for a "did both schemas get migrated" health check
respectively. Update the first to truncate `AppDbContext.IdentitySchema` and `.PlatformSchema` only
(the two schemas left); the second to assert on the same pair rather than on `TodoSchema`.

**If a database has already had `AddExampleFeatures` applied to it**, deleting the file does not drop
`todo`/`reminders` there — it only stops a fresh database from ever creating them. Dropping the
schemas from a database that already has them still needs a real migration generated the way
`docs/ADDING-A-FEATURE.md` documents (`dotnet ef migrations add DropExampleFeatures`, reviewed for
exactly two `DropTable`/`DropSchema`-equivalent groups and nothing else touched on a surviving
table). The tool that would generate it is pinned by `.config/dotnet-tools.json` (`dotnet-ef`
`10.0.10`) but that exact package version failed to restore in this environment with an unrelated
packaging error (`Settings file 'DotnetToolSettings.xml' was not found in the package`); a globally
installed `dotnet-ef 9.0.2` worked without incident, after warning that it was older than the
project's runtime.

## What remains, and why

Everything not named above stays, and every one of these was exercised, unmodified, by a full test
run after the removal:

- **`Auth`** (registration, login, refresh, logout, password reset, email confirmation, email
  change) — built on ASP.NET Identity, and touches neither example feature anywhere.
- **`Maintenance`** — the two purge use cases and the endpoint/worker that call them.
- **Idempotency** (`Api/Common/Idempotency/`, `Application/Common/Idempotency/`,
  `Infrastructure.Persistence/Common/Idempotency/`) — the claim/complete/release mechanism itself is
  generic. What disappears is the *demonstration* of it; see below.
- **Health checks, observability, rate limiting, security headers, request logging redaction,
  default-deny authorisation as a fallback policy, RFC 7807 problem details** — all cross-cutting,
  all still wired, all still tested — again, through Auth and Maintenance instead.

## What has no replacement

This is the part worth budgeting time for. Every item below compiles fine and fails a test loudly,
by design — these architecture tests were written to refuse to pass over an empty set rather than
pass silently — but "fails loudly" is not the same as "still demonstrated somewhere." None of the
following has a substitute anywhere else in the template once both example features are gone:

- **`IUnitOfWork`.** The port a use case calls to commit its work. `Auth`'s use cases commit through
  ASP.NET Identity's own `UserManager`, and `Maintenance`'s purges commit through
  `IIdempotencyStore` / `IRefreshTokenMaintenance` — neither ever calls `IUnitOfWork` directly. It
  was consumed **only** by the to-do list and reminder use cases and their domain-event consumers.
  `PortConventionTests.EveryApplicationPort_HasAConsumerInTheApplicationLayer` catches exactly this:
  with both examples gone, this fundamental cross-cutting abstraction has zero callers in the
  application layer.
- **The aggregate tracker / identity-map pattern** (`TodoListTracker`, `ReminderTracker`, and the
  three-contract registration trick documented on `PersistenceModule`). With both gone there is no
  tracker in the composed container at all —
  `SharedInstanceRegistrationTests.EveryAggregateTracker_ResolvesAsOneInstanceUnderEveryContractItServes`
  finds zero, not fewer.
- **Optimistic concurrency over HTTP** — `Versioned<T>`, the strong `ETag`, `If-Match`/`If-None-Match`,
  `412`/`428`. `TodoLists` is not the only demonstration of this: `Reminders`' reschedule (`PUT`)
  and cancel (`DELETE`) endpoints also read `If-Match` and answer `412`/`428` through the same
  `ApiControllerBase.ReadPrecondition`. Removing `TodoLists` alone still leaves the conditional-GET
  round trip (`Caching/CacheHeaderTests.cs`) with nothing to exercise, since only `TodoLists`' detail
  endpoint publishes an `ETag` a client can revalidate against — but removing `Reminders` too is what
  empties this demonstration out completely, taking the `If-Match` writes with it.
- **The default-deny fallback authorisation policy.** `Program.cs` sets a `FallbackPolicy` requiring
  an authenticated user precisely so an action that forgets `[Authorize]` is still denied. Checking
  that path over HTTP needs a controller that relies on it — and `TodoListsController` is not the
  only one: every action on it and on `RemindersController` declares neither `[Authorize]` nor
  `[AllowAnonymous]`, so both rely entirely on the fallback. `AuthController` decorates every action
  explicitly, and `MaintenanceController`/`AccountAdministrationController` each declare their own
  policy at the controller level; none of the three ever reaches the fallback. The rule that
  enumerates every verb on `TodoListsController` to prove the fallback actually denies
  (`DefaultDenyAuthorizationTests`) has no controller left to enumerate once both `TodoLists` and
  `Reminders` are gone.
- **Domain events, at all.** Once both features are gone, `AppTemplate.Domain` declares no
  aggregate, no entity, no value object and no domain event — `Features/` is empty, `Common/` holds
  only the primitives a real feature would build on. Five rules in `DomainModelTests` exist to prove
  properties of a concrete domain model; none of them has one to check. Neither does `Auth`: it never
  raises a domain event, so the cross-feature dispatch this template demonstrates — two independent
  consumers reacting to one event, one of them registered by a different feature entirely — has no
  live example either.
- **`ICollectionPolicy` / the sortable, filterable, paginated collection endpoint.** `TodoLists`' list
  endpoint was the only one that ever declared a collection policy. Two rules in
  `CollectionContractTests` exist to check that policy's internal consistency and its exemption from
  the constructor rule; with none registered, both are vacuous.
- **Ownership isolation for a resource addressed by id.** 404-not-403 for another user's resource —
  the deliberate choice that a 403 would leak that the id exists — has no surviving example: no
  action anywhere in the remaining API is addressed by an `{id}` route segment at all. Every
  authentication action addresses either nobody (a fixed path) or the caller identified by their own
  token; every maintenance action addresses everybody.
- **The `[Idempotent]` demonstration.** All three `[Idempotent]` actions this template ever shipped —
  `TodoListsController.Create`, `.AddItem`, `RemindersController.Schedule` — lived in one of the two
  example features. The mechanism (`IdempotencyFilter`, `IIdempotencyStore`, the claim/complete/release
  state machine) is untouched and still unit-tested directly, but the integration suite that drove it
  end-to-end over real HTTP (`Idempotency/IdempotencyTests.cs`) has nothing left to call and was
  deleted rather than adapted.

None of this is a defect in the removal. It is what "the examples were teaching the architecture, not
decorating it" actually costs once you take the lesson away: several of the tests written to prove
these mechanisms work were written against the only place they were ever exercised. The honest fix
in every case above was the same one, applied by hand to each test file: skip the ones that assert a
property of a domain model or a registration that no longer exists, with a comment naming exactly
what would bring it back (a real feature's first aggregate, its first collection endpoint, its first
version-conditioned mutation); retarget the ones that only ever needed "some authenticated endpoint"
at `GET /api/v1/auth/me` or another `Auth` action; delete the ones — `Idempotency/IdempotencyTests.cs`,
`Security/OwnershipIsolationTests.cs` — that had no substitute at all. Every one of those decisions is
in the diff, with the same reasoning repeated as a comment at the point it applies.

## Expected test fallout, and how it was resolved here

Beyond what is listed above, the removal breaks a long tail of smaller things, entirely by count or
by name rather than by missing mechanism. In the order they were hit:

- **Use-case and validator counts.** `ServiceRegistrationTests._knownUseCaseCount` (44 → 24 —
  read the current value rather than trusting this one; the authentication vertical has grown twice
  since it was written),
  `PortConventionTests`, `UseCaseConventionTests` and `LayoutConventionTests` all state a number
  either in an assertion or in the comment beside it. Every one of them needed its number and its
  comment updated together — the number alone, without the comment explaining what it now counts,
  is exactly the kind of assertion this template's own conventions warn against.
- **`ServiceRegistrationTests.TodoListAccess_IsRegisteredAsScoped`** and its `BuildProvider` fixture's
  `ITodoListRepository`/`IReminderRepository`/`IReminderNotifier`/`IReminderTargets`/`IReminderDiagnostics`
  substitutes — deleted outright; there is no vertical left for the first to assert about, and the
  fixture no longer needs to satisfy ports that no use case takes.
- **`ContainerCompositionTests`, `AdapterVisibilityTests`** — drop `ITodoListRepository` and
  `ITodoListQueries` from the hand-kept port lists; lower `AdapterVisibilityTests`'s non-vacuity
  floor from 10 adapters to 8 (a unit of work, a clock, an email sender, and the five authentication
  adapters — the repository and the query service are gone).
- **Test doubles that build real aggregates** — `Tests/Application/AppTemplate.Application.UnitTests/TestDoubles/{ATodoList,AReminder}.cs`
  — deleted; nothing left references either.
- **`VersionPreconditionTests`** kept its first half (the precondition object's own logic, which
  needs no aggregate) and dropped its second (a theory over the five to-do-list mutating use cases,
  proving each one actually applies the precondition it is handed) — there is no mutating,
  version-conditioned use case left anywhere to run that theory against. See
  [What has no replacement](#what-has-no-replacement).
- **`AuditableTests`** exercised `IAuditable` through `TodoList`, the template's one concrete
  aggregate. Rewritten against a private nested test-only aggregate implementing `IAuditable` the
  same way every real one did (public getters, explicit interface setters) — the interface itself
  needed no feature to stay covered.
- **`DomainEventDispatcherTests`, `DomainEventDispatchSaveChangesInterceptorTests`** — both raised a
  real `TodoListCreatedDomainEvent`/`TodoItemCompletedDomainEvent` through a real `TodoList` and a
  real `TodoListTracker`. Rewritten against a private in-file `IDomainEvent` record and a minimal
  `IDomainEventSource` implementation the test raises events into directly — the dispatcher and the
  interceptor are both generic over the interface and neither needed a feature to prove.
- **`ControllerContractTests`**'s deliberately-leaking test controller returned `TodoItemDto`;
  repointed at `LoginOutcome`, an application type from a vertical that survives.
- **Everything under `Tests/Integration/AppTemplate.Api.IntegrationTests/Security/`, `Caching/`, `Http/`** that used
  `TodoListsRoute` purely as "some authenticated endpoint" — repointed at `GET /api/v1/auth/me`
  (reads) or `POST /api/v1/auth/logout-all` (writes with no body). `RequestBodySizeLimitTests`
  repoints at the anonymous `POST /api/v1/auth/register` instead, which needs no session set up
  first. `FrameworkProblemDetailsTests`'s "an authored error keeps its code" case moved from a 404
  (`todoList.notFound`) to a 409 (`auth.register.unavailable`, raised by registering the same address
  twice) — same property, different status, because no authored 404 exists anywhere outside the
  removed features.
- **`IntegrationTestBase`** loses its `TodoListsRoute` constant and its "Todo lists"/"Conditional
  requests" regions (`CreateTodoListAsync`, `AddTodoItemAsync`, `LoadTodoListAsync`, `ReadETagAsync`,
  `RenameAsync`) outright — there is no generic replacement to leave in a shared base class for
  helpers that specifically create, version and conditionally mutate one aggregate.
- **`ApiFactory`/`ApiFixture`/`RecordedDomainEvents`** — the test host registered a *second* consumer
  of `TodoItemCompletedDomainEvent` purely to prove the dispatcher reaches every consumer of an
  event, not just the first. `RecordedDomainEvents.cs` is deleted along with the registration; the
  mechanism it proved is still covered at the unit level by `DomainEventDispatcherTests`.
- **`RegistrationFlowTests`** had a redundant assertion — "the token works" against `TodoListsRoute`,
  immediately followed by the same proof against `/auth/me` for a different reason (reading the
  profile). Removing the first left the second doing both jobs; nothing was lost.

## If you are keeping one and removing the other

**Removing `Reminders`, keeping `TodoLists`:** exactly the Reminders-only rows and edits above,
nothing from the TodoLists column. `TodoLists` never references `Reminders`. Every generic mechanism
above — `IUnitOfWork`, the tracker pattern, `ETag`/`If-Match`, the default-deny fallback, domain
events, `ICollectionPolicy`, ownership isolation, `[Idempotent]` on `Create`/`AddItem` — stays
demonstrated through `TodoLists` alone; only the `[Idempotent]` count and a handful of "N reminder
use cases" comments need updating.

**Removing `TodoLists`, keeping `Reminders`:** not a smaller version of the full removal — it does
not compile. `Reminders` calls `ITodoListQueries` and consumes `TodoItemCompletedDomainEvent`
directly, and `ReminderTargets` queries `context.TodoLists`. Either bring `TodoLists` back, or accept
that removing it means removing `Reminders` too and rewrite `Reminders`' three touch points
(`ScheduleReminderUseCase`, `CancelRemindersOnTodoItemCompletedConsumer`, `ReminderTargets`) against
whatever replaces the to-do item as the thing a reminder is scheduled against — which is no longer
"removing the example", it is redesigning the second one.

## Verification

Everything above was checked in a disposable copy of the repository, never in this one:

- `dotnet build AppTemplate.sln` — 0 warnings, 0 errors, both with only `Reminders` removed and with
  both examples removed.
- `dotnet test AppTemplate.sln` on every project — every unit and architecture test project passes
  or explicitly skips (with a reason naming this document); the integration suite, run against a
  real PostgreSQL container via Testcontainers, passed 91 of 92 tests unmodified, the one failure
  being unrelated to either example feature.
- `dotnet ef migrations has-pending-model-changes` (the check `PendingModelChangesTests` also runs)
  clean after the generated migration.

If your own removal produces a different diff than the one described here, the discovery-based
architecture tests (`RequireTypes`, the various `ShouldNotBeEmpty` non-vacuity guards) are what will
tell you first, and loudly — that is what they are for.

# Separating authentication — plan of record

**Status:** agreed, deferred. **Decided:** 2026-09-08. **Not started.**

Scheduled after wave 7 of `docs/plans/SDK-SPLIT-PLAN.md`. This document is self-contained on
purpose: everything it asserts about the tree was measured on 2026-09-08 and is stated with the
path and the count, so the analysis is never redone. Where a claim is a judgement rather than a
measurement, it says so.

This directory is exempt from `Tools/CheckDocPaths.cs`, so paths below may name a tree that does
not exist yet.

## The goal

Authentication becomes a module that could run in its own process, so that several applications of
one suite can share the same accounts. The principle behind it: **an application can have a
business user without having authentication at all.** The business user and the authenticated user
are two notions, and the business side must not know the second exists.

**No separate API is built here.** What is built is the boundary that makes one possible later:
authentication owns its own storage, its own migrations history and its own contract, and nothing
business-side reaches across.

## What is already true — measured, do not re-measure

The separation is further along than it looks, and three of the four things a split usually has to
fix are already right.

| Claim | Evidence |
|---|---|
| The business domain knows only an opaque owner | `TodoList`, `Reminder` and `StoredFile` each hold a bare `Guid OwnerId`. `Src/Domain/AppTemplate.Domain/` has no reference to EF Core or to ASP.NET Identity, and architecture rules assert it |
| There is **no foreign key** from business tables to the identity tables | The only `HasForeignKey` calls under `Persistence/Features/{TodoLists,Files,Reminders}/` are intra-aggregate: `TodoItemId`, `StoredFileId`. `OwnerId` is an indexed column with no navigation |
| The caller is already abstracted from the scheme | `Application.Core/Common/Ports/ICurrentUser.cs` exposes `Guid? UserId` and nothing else |
| The 20 auth ports are a contract, not a coupling | `Application.Auth/Features/Auth/Ports/` declares them; `Infrastructure.Identity` implements them and nothing else does |
| **No transaction spans an identity write and a business write** | No file under `Src/Application/AppTemplate.Application.Auth/` names `IUnitOfWork`. The one auth type that does is `Infrastructure.Identity/Features/Auth/Services/RefreshTokenGrantsService.cs`, which commits refresh-token rows through it; its own summary describes the shared transaction as something a rotation *could* use. No code path uses it |
| The migrations are already sorted by concern | `20260809002532_InitialCreate` creates the 9 identity tables plus `DataProtectionKeys` (schema `identity`) **and `IdempotencyKeys` (schema `platform`)**. The other three — `AddExampleFeatures`, `AddFiles`, `AddStoredFileTags` — are business only. The only non-auth table in the initial migration is the idempotency store, which belongs to no feature |
| Almost all of `Persistence/Common/` is agnostic | Occurrences of `AppDbContext` per file: `Saving/Auditing/AuditingSaveChangesInterceptor.cs` 0, `Saving/DomainEvents/DomainEventDispatcher.cs` 0, `Saving/Tracking/AggregateTracker.cs` 0, `Leases/PostgresLeaderLease.cs` 0, `Time/SystemDateTimeProvider.cs` 0, `Options/DatabaseOptions.cs` 1, `Contexts/DefaultConnectionString.cs` 1, `Saving/EfUnitOfWork.cs` 2, `Idempotency/IdempotencyStore.cs` 4 |

What is missing is therefore not isolation. It is that **the concept has no name** — `OwnerId` is an
anonymous `Guid`, so a reader navigating the domain cannot tell that it is a subject identifier
handed over by whoever authenticates rather than a key into a local table — and that the two halves
share one `DbContext`, one migrations history and one unit of work.

## The couplings to break

| Coupling | Measured volume | Where |
|---|---|---|
| One context for both halves | `AppDbContext : IdentityDbContext<AppUser, AppRole, Guid>`, plus `Features/Identity/` = **18 files** (3 models, 9 configurations, 4 seeding, 3 grant-table) | `Src/Infrastructure/AppTemplate.Infrastructure.Persistence/` |
| The identity stores and the key ring name the shared context | `AddEntityFrameworkStores<AppDbContext>()` and `PersistKeysToDbContext<AppDbContext>()` | `Infrastructure.Identity/AuthModule.cs:229` and `:302` |
| Refresh-token rows commit through the shared unit of work | 4 calls to `unitOfWork.SaveChangesAsync` | `Infrastructure.Identity/Features/Auth/Services/RefreshTokenGrantsService.cs` |
| The seeder is declared by the business module and consumed by the API host | registered at `PersistenceModule.cs:199`, consumed at `DevelopmentDatabaseExtensions.cs:41` | two projects |
| The one business-to-auth read | **1 file**: it resolves `profile.UserName` and `profile.Email` through `IUserProfilesService` | `Infrastructure.Email/Features/Reminders/EmailReminderNotifier.cs` |
| Migration and health-check plumbing name one context | `DevelopmentDatabaseExtensions.cs:39`, `Api/Program.cs:106` (`AddDbContextCheck<AppDbContext>`) | the API host |
| The auth HTTP surface sits in the business host | **25 files, 889 lines**: `AuthController` 477, `AccountAdministrationController` 112, `AuthResponseMapping` 96, plus contracts | `Src/Presentation/AppTemplate.Api/Features/Auth/` |
| The in-memory double | 3 files, an external-identity verifier | `Infrastructure.InMemory/Features/Auth/` |
| Auth maintenance runs from the shared loops | 2 call sites for `PurgeExpiredRefreshTokens` | `Api/Features/Maintenance/`, `Worker/Features/Maintenance/` |

## Decisions

### A1. There is no `AppTemplate.Domain.Auth`

**Decided.** No auth domain project is created.

**Reason.** No auth domain exists to move. The model is ASP.NET Identity's: `AppUser` and `AppRole`
are persistence models under `Persistence/Features/Identity/Models/`, and every rule about
passwords, lockout, two-factor enrolment and token providers lives either in Identity's own
framework code or in `Infrastructure.Identity`'s services. The one `internal` type in
`Application.Auth` is `PasswordPolicy`, consumed from within that project.

**Rejected — creating it for symmetry.** It would hold a folder of types with no invariant, which
is shape without substance. `CONTRIBUTING.md` forbids a guessed abstraction, and decision 32 of
`docs/plans/SDK-SPLIT-DECISIONS.md` rejected exactly this move under the name "inventing a concept
with no existing code behind it".

**Rejected — taking ownership of the identity model back from ASP.NET Identity.** It is the only
route that would populate such a project honestly, and it means owning password hashing, lockout,
two-factor and token generation. That is a project of its own with a security surface a template
should not teach anyone to re-derive. Reconsider only if the module stops using ASP.NET Identity.

**Consequence.** Authentication is a module with a contract and its own storage, not a fourth
layer stack. The boundary is expressed in infrastructure and in the port surface that already
exists.

### A2. The business user gets a name, and it costs no migration

**Decided.** A `UserId` value object in `AppTemplate.Domain.Core/Common/Primitives/`, returned by
`ICurrentUser` and held by the three aggregates in place of the bare `Guid`.

**The name is `UserId`, and fork 1 is closed** (owner, 2026-09-08). `SubjectId` says more
precisely what the thing is, and the reason below argues for that reading; what decides it is that
`ICurrentUser.UserId` and the three `OwnerId` properties already say *user*, and a type whose name
disagrees with every property that holds it introduces a second word for one concept. The
precision `SubjectId` would buy is bought instead by the type's own summary.

**Reason.** The distinction the owner asked for is already true of the data and invisible in the
code. A named type is what makes it readable by navigation: the type says that this is a subject
identifier supplied from outside, and a reader meets it before meeting any auth code. It is also
what a separate auth service hands over — a subject claim — so the name stays accurate on the day
the module moves.

**Measured cost, as predicted.** The column is `uuid` either way and no foreign key exists, so **no
migration**. The edit is the three aggregates, their events, the three persistence mappers,
`ICurrentUser` and its two adapters, and the tests that name the `Guid`.

**Measured cost, as it turned out: 111 files.** That list counts where the `Guid` is *declared* and
not where it *flows*, which is the larger half. A type at a boundary forces a decision at every
other boundary it reaches, and the plan named none of them. What the work settled, and why:

- **The business ports take `UserId`** — the four query ports of `AppTemplate.Application`, plus
  `UsedTagsCache.KeyFor`, `ReminderNotification` and the in-memory `SentReminderNotification`. Eleven
  files, and it is what keeps `.Value` out of the business use cases entirely: they hand the owner
  straight through. The EF implementations compare the column to `ownerId.Value`, which is the same
  place the mappers already convert.
- **The twenty authentication ports keep the raw `Guid`.** What identifies an account there is the
  subject a token carries, not the business's notion of an owner, and `AppTemplate.Application.Auth`
  is the project a derived application replaces wholesale — pushing a domain primitive into its port
  surface would be the coupling this chantier exists to remove. The cost is 19 sites reading
  `userId.Value.Value`, and that is **deliberately** left visible: they are exactly the places where
  a named owner is handed to an identity port, which is the seam wave C moves. Zero such sites exist
  in the business half.
- **`IAuditActor` and `IAuditable` keep `Guid?`.** `IAuditActor`'s own documentation says it is
  deliberately not `ICurrentUser` and answers a different question — whom do we record, not who is
  calling — so `CurrentUserAuditActor` converts with `currentUser.UserId?.Value` and the audit
  columns stay what they are: a persistence record of who last wrote a row.
- **`IdempotencyKey` keeps `Guid`.** It scopes a key to a caller and is host machinery; the filter
  unwraps once.

**What the type paid for immediately.** Three `ownerId == Guid.Empty` guards, one per aggregate,
are gone: the invariant is stated once in `UserId.Create` and the aggregates take
`ArgumentNullException.ThrowIfNull(ownerId)` beside the guards they already had. Six tests named
`*_Rejects_AnEmptyOwnerId` therefore changed subject to an absent owner, and
`CurrentUserExtensionsTests.RequireUserId_Fails_WhenTheIdIsEmpty` became unreachable and was
removed — with a note at its neighbour saying where the guarantee now lives, because a deleted test
nobody explains reads like a lost one. `UserIdTests` adds eight in its place, and the suite went
from 3328 to 3335.

**One wart, worth knowing before reading the code.** The property `ICurrentUser.UserId` and the type
`UserId` share a name, so inside any class holding such a member the simple name binds to the
member and the factory call has to be qualified. That is three places — the HTTP adapter and two
test stubs — each carrying a one-line note. `SubjectId` would not have had this problem, and fork 1
was decided on other grounds; this is the price of that decision, and it is small and local.

**Rejected — a business `User` entity.** It would need attributes the business does not have. The
business owns no fact about a person today; the moment it does, that entity is written then.

### A3. The agnostic EF mechanisms move to `AppTemplate.Infrastructure.Core`

**Decided.** The auth module composes its own context over the same mechanisms the business module
uses, taken from `AppTemplate.Infrastructure.Core`.

**Reason.** A second context needs the same unit of work, the same three interceptors and the same
aggregate tracker. `ModuleDependencyTests.InfrastructureModules_ReferenceOnlyPersistenceHorizontally`
forbids reaching sideways for them, so without a shared project the choice is to duplicate them or
to leave the auth module dependent on the business persistence module — which is the coupling this
plan exists to remove.

**Precondition, and it is met elsewhere.** `Infrastructure.Core` is created in wave 6 of the SDK
split, justified there by a different measured duplication: the email-template engine exists twice
(`Infrastructure.Identity/Features/Auth/Factories/EmailBodyFactory.cs`, 196 lines, and
`Infrastructure.Email/Features/Reminders/ReminderEmailTemplate.cs`, 131 lines), with `RenderedEmail`
declared in both. So this plan inherits the project rather than creating it.

**What moves here, and what it costs.** Of the files listed in the measurement table above, seven
name `AppDbContext` zero times and are pure moves. Two must become generic over `DbContext`:
`EfUnitOfWork` (2 occurrences) and `IdempotencyStore` (4). `AppDbContext` itself stays in the
business module — it is the business model's composition root and the one type in a `Common/`
allowed to name a feature.

**Rule amendment required.** "An infrastructure module references only Persistence horizontally"
becomes "only Persistence or the infrastructure `.Core`". The non-vacuity assertion in that rule
stays, and the amendment is what makes the shared foundation legal without opening sideways
references generally. **No amendment was in fact needed:** it had already been made when the
infrastructure layer got its agnostic half, so the move landed against a rule that already
permitted it.

**What the move measured, against what this entry predicted.** Both counts here were wrong. Of the
21 files in `Persistence/Common/`, **14** name `AppDbContext` zero times — not the seven this entry
claims, and not the five the measurement table above lets a reader count, which was a sample rather
than the set. The criterion that decided the move is the one this entry states, and it is need
rather than agnosticism: what a second context has to compose.

So **12 files moved**, eleven of them unchanged:

- `EfUnitOfWork`, generic over `DbContext` — the one genericisation.
- The three interceptors, the domain-event dispatcher and its two contracts, the
  `AggregateTracker` base and its two contracts, and `StoredStamps`.
- `SystemDateTimeProvider`. **It travels and the Postgres lease does not**, which is not a blanket
  answer because the two do not cost the same. The clock implements a cross-cutting port, which is
  the shape `HybridCacheStore` already has in this project, and the auth module will need one for
  token lifetimes and lockout windows — leaving it behind would force that module to duplicate it or
  to depend on the business persistence module, which is the coupling this plan removes.
  `PostgresLeaderLease` takes a session-level advisory lock and would pull **`Npgsql` into a
  package-grade project every module references**, for a mechanism no second context needs. That is
  the same reasoning that keeps the PostgreSQL driver out of `AppTemplate.Presentation.Core`.

**The idempotency store did not move, so there is one genericisation and not two.** It owns a table:
`IdempotencyRecordConfiguration` calls `AppDbContext.PlatformSchema`, so moving it forces a decision
about which context owns `IdempotencyKeys` and where that schema name lives — which is A4's and A5's
subject, not this move's, and the measurement above notes the table sits in the initial migration A5
regenerates. The port is cross-cutting and the table belongs to no feature, so the move is defensible
eventually; doing it here would have pre-empted A4 with a guess and bought the second context
nothing.

**The seam this entry did not foresee, and it is the interesting part.** Nearly every moved type was
`internal`, so the persistence module could no longer see them from another assembly. Making them
public broke `AdapterVisibilityTests.Adapters_ImplementingAnApplicationPort_AreInternalToTheirModule`
— and the answer was already in this project, two files away: `HybridCacheStore` is `internal` and
composed by a public `AddCacheStore()`. So the adapters stay internal and the project exposes calls
instead: `AddCoreSaving<TContext>()`, `AddCoreSavingInterceptors()` and `AddSystemClock()`. **The
rule needed no exemption**, the public surface came out at 30 entries rather than the 51 the
promote-everything shape produced, and the context arrives as a type argument — which is exactly the
seam wave C's second context calls with its own.

What stayed public is what a module cannot delegate: the `AggregateTracker` it derives from, the
`StoredStamps` its mapper calls, the `IAuditActor` its host answers, and the three interceptors it
attaches to its own context. Those are not adapters of application ports, so the rule does not reach
them, and making them internal would have cost two large tests their subject.

**One nullability finding worth keeping.** `protected IReadOnlyCollection<TrackedAggregate>` on a
generic type raises `RS0041`: the analyser cannot annotate an unqualified nested-type reference, and
this repository has never accepted an oblivious public API — there is not one `~` entry in any
baseline. Naming the outer type arguments —
`IReadOnlyCollection<AggregateTracker<TAggregate, TRecord>.TrackedAggregate>` — annotates it and
costs nothing but a longer signature. **Offered and not taken:** exposing the live pairs instead
would have removed both the obliviousness and the skip-removed-rows loop that the type's own summary
admits is identical in every feature. It is a design change, and this was a move.

**Two tests moved and two stayed, on what they actually exercise.** The dispatcher's test and the
unit of work's moved, and both had to shed a product type to live in a package-grade mirror: the
first raised a real `TodoLists` event where the dispatcher only ever keys on an event's runtime
type, the second built an `AppDbContext` where EF only needs *a* provider to construct a context at
all. Each now carries its own. `AuditingSaveChangesInterceptorTests` and
`DomainEventDispatchSaveChangesInterceptorTests` stayed: they exercise the mechanism composed over
this module's context and its feature rows, which is the module's subject, and rewriting them
against local doubles would have cost the auditing interceptor the property that it is proven
against the real model.

### A4. Two contexts, two histories, one database

**Decided.** `AuthDbContext` in the auth module, with its own migrations history table and the
`identity` schema. `AppDbContext` keeps the business schemas and `platform`. One database, one
connection string.

**This reverses a decision recorded in `docs/ARCHITECTURE.md`**, section "One DbContext, one
database, five schemas", which argues for a single context on two grounds: a second context buys a
boundary the schema already gives, and one context allows a transaction spanning an identity write
and a business write.

**Why the reversal is legitimate, and it is a change of premise rather than of judgement.** The
first ground answered the question "how do we stop a domain entity acquiring a navigation property
to `AppUser`". That question is settled by other means and stays settled — EF does not map the
domain entities at all. The question here is different: can authentication be deployed on its own.
Under that question a separate migrations history is not a cost without a benefit, it is the
deliverable.

**The second ground is measured, and it is unused.** See the measurement table: no code path commits
an identity write and a business write together. The capability is described in
`RefreshTokenGrantsService`'s own summary as available rather than used. So the reversal gives up a
possibility, not a behaviour.

**The cost that is real, and accepted.** Two histories can disagree about what has been applied,
which is a deployment state a single history makes unreachable. It is also the state any separate
deployment of the module implies, so it is paid now or on the day of the move.

**`docs/ARCHITECTURE.md` is rewritten, not patched**, for that section: its title, its table and
both "why" paragraphs describe a single-context arrangement.

### A5. The migrations are regenerated, not split

**Decided.** Two fresh initial migrations, one per context, rather than an attempt to carve
`InitialCreate` in two.

**Reason.** A template ships no deployed database, so there is no applied history to preserve;
`docs/DEPLOYMENT.md` and the development bootstrap both apply migrations from empty. Carving the
initial migration means hand-editing a generated file and its designer snapshot, which the tooling
regenerates correctly for free.

**What each one contains.** Auth: the 9 identity tables, `DataProtectionKeys`, `RefreshTokens`.
Business: `IdempotencyKeys`, then the three feature migrations, which need no change — they name
only business schemas.

**Consequence to state in the removal guide.** A derived project that has already deployed the
template cannot take this change without a migration of its own. That is a note in
`docs/REMOVING-THE-EXAMPLE-FEATURES.md` and `docs/DEPLOYMENT.md`, not a blocker here.

### A6. The reminder notifier keeps its lookup, and the alternative is priced

**Decided.** `EmailReminderNotifier` goes on resolving the owner's address through the auth module's
profile port. It becomes the single, named business-to-auth door, documented in
`docs/ARCHITECTURE.md`.

**Reason.** The owner's instruction is that no separate auth API is built now, and the two
alternatives both cost more than the coupling does while it stays in-process.

**Rejected for now — the reminder copies the contact at scheduling time.** This is the honest
service-boundary answer and it is deferred rather than refused: it removes the read entirely, at
the price of a migration adding two columns to `Reminders`, and of a stored address that does not
follow a later change. Offer it again on the day the module moves out of process, when it stops
being optional.

**Rejected — a business-declared port implemented by the auth module.** It looks like the clean
inversion and it breaks a rule:
`PortConventionTests.EveryApplicationPort_HasAConsumerInTheApplicationLayer` requires a port
declared in the application layer to have a consumer *in* that layer, and nothing business-side
needs a user's contact details. This is the trap decision 5 of `docs/plans/SDK-SPLIT-DECISIONS.md`
records for `IAuditActor`, in the same shape.

### A7. `AppTemplate.Api.Auth` is a separate question, taken last

**Decided.** The auth HTTP surface stays in `AppTemplate.Api` through waves A to D. Whether it
becomes its own presentation project is decided afterwards, on a measured tree.

**Reason.** Decision 1 of `docs/plans/SDK-SPLIT-DECISIONS.md` rejected it, and priced three costs:
a cross-assembly `<see cref>`, an application-part discovery question, and an ordering constraint
against the per-version OpenAPI loop that stays invisible until a v2 exists. The owner's goal
changes the premise of that rejection — if authentication is to leave, its controllers are its HTTP
surface. But the 25 files are a move next to what waves B and C do, and the lift is enabled by the
storage split rather than by the project boundary.

**What decides it later.** Whether the controllers can be discovered from a class library without
the host naming them one by one, and what `AddCoreOpenApiPerVersion`'s absence (decision 28) implies
for a document assembled from two assemblies.

### A8. The auth module gets its own unit of work

**Decided.** `RefreshTokenGrantsService` commits through a unit of work over `AuthDbContext`, taken
from `Infrastructure.Core`'s generic mechanism.

**Reason.** `IUnitOfWork` resolves the business context. Once the grant table lives in the auth
context, that commit reaches the wrong model, and the failure is a silent no-op rather than an
error: `SaveChangesAsync` on a context with no tracked changes succeeds.

**This is the one behavioural risk of the whole plan.** The test that proves it is
`Tests/Integration/AppTemplate.Infrastructure.Auth.IntegrationTests/Leases/` and
`.../RefreshTokenRotationTests.cs`, plus `GrantTableFixture`, which compose the module against a
real PostgreSQL. Run them before and after, and read the row counts rather than the pass count.

## Waves

Sequential. Each ends green on the full gate of `docs/plans/SDK-SPLIT-PLAN.md`.

### Wave A — name the subject. **Done, 2026-09-08.**

`UserId` in `Domain.Core/Common/Primitives/`, which takes that project from seven files to eight and
adds eleven symbols to its public surface. The three aggregates, their events, the three mappers,
`ICurrentUser` and its two adapters — plus the boundaries A2 now records. No migration, no project
created. Green on the full gate: 3335 tests with none failing, 0 build warnings, all six packages,
both images, every gate and `dotnet format --verify-no-changes` clean.

**One thing it changed that no rule watches.** `AppTemplate.Application.Auth`,
`AppTemplate.Infrastructure.Email` and `AppTemplate.Presentation.Core` now name
`AppTemplate.Domain.Core` in their manifests while declaring only `AppTemplate.Application.Core`.
Nine of the fifteen source projects are in that position now, and none declares the reference. It is
**pre-existing rather than introduced here** — `AppTemplate.Infrastructure.Persistence` has always
named `IAuditable` this way — and the principle `docs/ARCHITECTURE.md` states about declaring a
reference rather than inheriting it is written about `AppTemplate.Application.Core` alone, which is
why the diagram draws ten arrows into that project and two into `Domain.Core`. The documents that
said `Application.Auth` "names no domain type at all" now say it names no *aggregate*, which is the
claim that is true and the one that mattered. **Whether the nine should declare the reference is a
question for the owner, not a thing this wave decided.**

### Wave B — move the agnostic mechanisms into `Infrastructure.Core`. **Done, 2026-09-08.**

Twelve files: eleven pure moves and one genericisation, plus three registration calls the plan did
not foresee, the vocabulary entries in `LayoutConventionTests`, and no rule amendment — A3 records
all of it with what was measured against what was predicted. The business module keeps
`AppDbContext`, the idempotency table, the Postgres lease and its feature folders, and composes what
it saves through by calling rather than by naming. Nothing about authentication changed.

Green on the full gate: 3335 tests with none failing, 0 build warnings, 131 architecture rules, all
six packages, both images, every gate and `dotnet format --verify-no-changes` clean.

### Wave C — authentication owns its storage. **Done, 2026-09-08.**

The heart of the plan, and green on the full gate: 3335 tests with none failing, 0 build warnings,
131 architecture rules, all six packages, both images, every gate clean.

**A8's risk fired, exactly as written.** `IUnitOfWork` is registered once, so the auth module's
writes were staged on `AuthDbContext` and committed on `AppDbContext` — a save with nothing tracked
succeeds and reports zero rows, so refresh-token rotation began answering 401 with no error
anywhere. Reading row counts rather than pass counts is what A8 told the next reader to do, and it
was the failing integration tests that said so first. The fix designs the ambiguity out instead of
watching for it: `IContextUnitOfWork<TContext>` is what a module owning a context takes, and the
unnamed port stays registered by the module owning the business context. Nothing else in the plan
was wrong about behaviour.

**Five things the plan did not carry, each with what it cost.**

- **Two contexts on one connection string share one pool, or two, depending on nothing visible.**
  Npgsql pools per connection string, so the two halves must build the identical string or a
  deployment silently gets two pools of `MaxPoolSize` instead of one. `DatabaseOptions` therefore
  moved to `Infrastructure.Core` and both modules bind the same section into it. **Rejected —
  putting the connection-string builder there too:** it would pull `Npgsql` into a package-grade
  project every module references, which is the reason the Postgres lease stayed out of it.
- **`DefaultConnectionString` moved as well**, because the auth module needed it and reaching into
  the business module for it would have kept the coupling this wave removes. Its own summary claimed
  "exactly one `AppDbContext`, so exactly one migrations history", which the wave made false.
- **No module references another any more**, so the horizontal permission for the persistence module
  describes nothing. The rule is rewritten rather than left with a dead branch, and the persistence
  module becomes a module like any other — which is what moving the saving mechanisms earned.
- **`verify` checked one context for pending model changes.** With two histories the omission is
  invisible: the other half's migrations apply cleanly while this half's tables are simply absent.
  Both are checked now, and the auth mirror has its own `PendingModelChangesTests` saying why.
- **The lease tests were in the auth integration project** because that project was historically the
  one with a database. The lease is the business module's, so they moved to the suite that composes
  the real host. **Rejected — a Persistence integration project:** structurally right, and a guid in
  two manifests for two files. **Rejected — referencing the business module from the auth test
  project:** the coupling this wave removes, one level up.

**Two counts in this document were wrong.** `Persistence/Features/Identity/` is 19 files, not 18 —
3 models, 9 configurations, 4 seeding, 3 grant-table, which is what the same entry adds up to. And
the container test's message had already predicted its own rewrite: it said that if the seeder
stopped holding the container up, "the caveat in docs/REMOVING-THE-EXAMPLE-FEATURES.md is answerable
at last". It is, and the test now asserts the *absence* of that coupling so it cannot come back
unnoticed.

- `Persistence/Features/Identity/` — 18 files — moves into the auth module.
- `AuthDbContext`, deriving from `IdentityDbContext<AppUser, AppRole, Guid>` and implementing
  `IDataProtectionKeyContext`, with the `identity` schema and its own migrations history table.
- `AppDbContext` stops deriving from `IdentityDbContext` and drops the 9 identity configurations,
  `RefreshTokens` and `DataProtectionKeys`.
- `AuthModule.cs:229` and `:302` name `AuthDbContext`.
- `RefreshTokenGrantsService` commits through the auth unit of work (A8).
- `IIdentitySeeder` and `IdentitySeeder` leave the business module.
- Two regenerated initial migrations (A5).
- The API host migrates and health-checks both contexts:
  `DevelopmentDatabaseExtensions.cs:39`, `Api/Program.cs:106`.
- The design-time factory gains a twin for the auth context.

**What this unblocks, and it is worth naming.** The two couplings that make
`ContainerCompositionTests.RemovingAuthentication_IsHeldUpByOneInfrastructureCoupling_NotByTheApplicationLayer`
true are `IIdentitySeeder` and `IReminderNotifier`. The first disappears by construction here — the
seeder leaves with the module. So the claim the correction inside decision 7 could not assert
becomes assertable except for the notifier, and A6 is the entry that keeps that honest: the test is
rewritten to name **one** coupling, not two, and its message says which.

### Wave D — close and document

`docs/ARCHITECTURE.md`'s context section rewritten (A4), the single business-to-auth door documented
(A6), the removal guide's note about an already-deployed derived project (A5), and the module table
in the architecture document. Then the `Api.Auth` question (A7), on the tree as it then stands.

## Blast radius

| Item | Work |
|---|---|
| `AppTemplate.sln`, `.template.config/template.json` | Two new projects across waves B and C, plus their mirrors: guids in both files, and `Rules/TemplatePackagingTests.cs` is what guards the list. `dotnet sln add` is neither idempotent nor predictable — read the file after, verify nesting by hand |
| Both Dockerfiles | The explicit `COPY` list gains one entry per new project. The Worker image is not built in CI, so build it by hand in the gate |
| `ModuleDependencyTests` | `InfrastructureModules_ReferenceOnlyPersistenceHorizontally` amended (A3); `Persistence_DependsOnNoModule` and `Persistence_ReferencesNoInfrastructureModule` re-read against the new shape; the anti-vacuity floor for modules raised with the count |
| `LayoutConventionTests` | Vocabulary entries for both new projects, in both dictionaries and in both directions; `ModuleFileName` accepts `InfrastructureCoreModule.cs` |
| `PersistenceModelTests`, `ExternalKeyTests` | Both read the persistence model. Re-point at whichever context owns each row type |
| `ContainerCompositionTests` | The composition list gains the auth module's own registration; the two-coupling test becomes a one-coupling test (wave C) |
| `LayerDependencyTests` | The forbidden lists are namespace prefixes; add the two new projects on both sides |
| `PortConventionTests`, `AdapterVisibilityTests` | Population reads a set of assemblies; add the auth module |
| `Tests/` mirrors | One mirror per new project, `Tests/` being a 1:1 mirror of `Src/`. Each `InternalsVisibleTo` names exactly one assembly — its own mirror |
| Integration fixtures | `ApiFactory`, `ApiFixture`, `TestDatabase` and `GrantTableFixture` each migrate or compose a context. Four files, and `GrantTableFixture` is the oracle for A8 |
| `docs/` | `ARCHITECTURE.md` (the context section, the module table, the layer diagram), `CONFIGURATION.md` (the connection-string section keeps one key, so check its prose is still true), `DEPLOYMENT.md` (two histories), `REMOVING-THE-EXAMPLE-FEATURES.md` (A5's note, and Auth removal gets genuinely simpler) |
| `coverage.minimum` | Re-measure at the end, the way the file's own history section prescribes |

## What is verified, and what is not

**Verified by measurement**, on 2026-09-08: every row of the two tables at the top, the per-file
`AppDbContext` counts, the migration contents, the absence of a business-to-identity foreign key,
the absence of an identity-plus-business transaction, and the file and line counts.

**Not verified, and to check before wave C starts:** whether any test asserts on the single
migrations history table by name; whether `Testcontainers` fixtures assume one `MigrateAsync` call;
and whether the data-protection key ring's move changes anything for tokens issued before it — a
template question only in that a derived deployment would have live key material.

**Judgement, not measurement:** A1 (no auth domain), A6 (which of the three doors to keep) and A7
(whether the controllers move). Each states its reasoning above so it can be disagreed with.

## Open forks left to the owner

1. ~~**Wave A's naming.**~~ **Closed: `UserId`** (owner, 2026-09-08). See A2 for what decided it
   against `SubjectId`.
2. ~~**Whether wave C also renames `AppTemplate.Infrastructure.Auth`.**~~ **Closed: it is
   renamed to `AppTemplate.Infrastructure.Auth`, in wave C** (owner, 2026-09-08). The module
   implements the auth ports and will own the auth storage, so the name matches the layer above it
   — `AppTemplate.Application.Auth` — and the `Features/Auth/` folder it already uses. It happens
   inside wave C rather than after it, because that wave already rewrites the module's storage and
   two renames of one project read worse than one.

   **The hazard to respect, and it is written down in `docs/plans/SDK-SPLIT-HANDOFF.md`:**
   `dotnet sln add` re-generates a different project guid when a project is removed and re-added,
   and `Rules/TemplatePackagingTests.cs` guards the guid list in `.template.config/template.json`.
   So the rename is: edit `AppTemplate.sln` by hand rather than through the CLI, keep the existing
   guid, then verify nesting by reading the file. The mirror test project renames with it, by the
   1:1 rule.
3. **A6 again, at the end of wave D:** keep the door, or take the denormalisation.
4. **A7:** `Api.Auth`, or the controllers stay.

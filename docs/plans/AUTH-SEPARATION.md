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
| The identity stores and the key ring name the shared context | `AddEntityFrameworkStores<AppDbContext>()` and `PersistKeysToDbContext<AppDbContext>()` | `Infrastructure.Identity/IdentityModule.cs:229` and `:302` |
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

**Reason.** The distinction the owner asked for is already true of the data and invisible in the
code. A named type is what makes it readable by navigation: the type says that this is a subject
identifier supplied from outside, and a reader meets it before meeting any auth code. It is also
what a separate auth service hands over — a subject claim — so the name stays accurate on the day
the module moves.

**Measured cost.** The column is `uuid` either way and no foreign key exists, so **no migration**.
The edit is the three aggregates, their events, the three persistence mappers, `ICurrentUser` and
its two adapters, and the tests that name the `Guid`.

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
references generally.

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
`Tests/Integration/AppTemplate.Infrastructure.Identity.IntegrationTests/Leases/` and
`.../RefreshTokenRotationTests.cs`, plus `GrantTableFixture`, which compose the module against a
real PostgreSQL. Run them before and after, and read the row counts rather than the pass count.

## Waves

Sequential. Each ends green on the full gate of `docs/plans/SDK-SPLIT-PLAN.md`.

### Wave A — name the subject

`UserId` in `Domain.Core/Common/Primitives/`. The three aggregates, their events, the three
mappers, `ICurrentUser` and its two adapters. No migration, no project created. Green on its own,
and independent of every other wave.

### Wave B — move the agnostic mechanisms into `Infrastructure.Core`

Seven pure moves, two genericisations, one rule amendment, and the vocabulary entries in
`LayoutConventionTests`. The business module keeps `AppDbContext` and its feature folders. Nothing
about authentication changes here.

### Wave C — authentication owns its storage

The heart of the plan.

- `Persistence/Features/Identity/` — 18 files — moves into the auth module.
- `AuthDbContext`, deriving from `IdentityDbContext<AppUser, AppRole, Guid>` and implementing
  `IDataProtectionKeyContext`, with the `identity` schema and its own migrations history table.
- `AppDbContext` stops deriving from `IdentityDbContext` and drops the 9 identity configurations,
  `RefreshTokens` and `DataProtectionKeys`.
- `IdentityModule.cs:229` and `:302` name `AuthDbContext`.
- `RefreshTokenGrantsService` commits through the auth unit of work (A8).
- `IIdentitySeeder` and `IdentitySeeder` leave the business module.
- Two regenerated initial migrations (A5).
- The API host migrates and health-checks both contexts:
  `DevelopmentDatabaseExtensions.cs:39`, `Api/Program.cs:106`.
- The design-time factory gains a twin for the auth context.

**What this unblocks, and it is worth naming.** The two couplings that make
`ContainerCompositionTests.RemovingAuthentication_IsHeldUpByTwoInfrastructureCouplings_NotByTheApplicationLayer`
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

1. **Wave A's naming.** `UserId` or `SubjectId`. `UserId` matches `ICurrentUser.UserId` and the
   `OwnerId` properties; `SubjectId` says more precisely what it is and matches what a token
   carries. Not chosen here.
2. **Whether wave C also renames `AppTemplate.Infrastructure.Identity` to
   `AppTemplate.Infrastructure.Auth`.** The module implements auth ports and would then own auth
   storage, so the name would match the layer above it — `AppTemplate.Application.Auth` — and the
   folder `Features/Auth/` it already uses. The cost is a rename across the solution manifest, the
   template manifest, both Dockerfiles, the mirrors and the docs.
3. **A6 again, at the end of wave D:** keep the door, or take the denormalisation.
4. **A7:** `Api.Auth`, or the controllers stay.

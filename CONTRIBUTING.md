# Contributing

This file is the working agreement for the repository. It is short on etiquette and long on the
things that will fail your build, because those are what cost time.

## Getting a green build

```bash
dotnet restore AppTemplate.sln
dotnet build   AppTemplate.sln
dotnet test    AppTemplate.sln          # needs Docker: see "Tests" below
```

Or, equivalently, through the task wrappers — they print the real command before running it, so the
script is also the documentation:

```bash
./tasks.ps1 restore
./tasks.ps1 build
./tasks.ps1 test                 # everything
./tasks.ps1 test -NoIntegration  # skips the Testcontainers suite
./tasks.ps1 hygiene              # doc paths and workflow structure; no SDK needed
./tasks.ps1 verify               # the whole gate, in CI's order
```

`tasks.ps1` targets Windows PowerShell 5.1 — what a stock Windows box ships — and runs unchanged on
PowerShell 7+ on any OS. Do not introduce 7-only syntax into it; requiring 7.0 means the script does
not start on the machine it exists to help.

Prerequisites: the .NET SDK version pinned in `global.json` (nothing else — `rollForward` is
`latestFeature`, so a newer patch of the same feature band is fine), and Docker for the integration
tests. `./tasks.ps1 compose-up` starts PostgreSQL and mailpit for running the app by hand.

## The gate

A change is done when all six of these pass. CI runs the same six.

```bash
dotnet build AppTemplate.sln                       # 0 warnings, 0 errors
dotnet test  AppTemplate.sln                       # all green
dotnet format AppTemplate.sln --verify-no-changes  # clean
dotnet ef migrations has-pending-model-changes \
  --project Src/Infrastructure/AppTemplate.Infrastructure.Persistence \
  --startup-project Src/Infrastructure/AppTemplate.Infrastructure.Persistence
dotnet list package --vulnerable --include-transitive   # reports nothing
./tasks.ps1 coverage                      # line coverage >= coverage.minimum
./tasks.ps1 hygiene                       # doc paths resolve, workflows are sound
```

The last one exists because two classes of defect are invisible to the compiler and to the tests:
a documented path that no longer resolves, and a workflow that would fail the first time it ran.
`.github/scripts/check-doc-paths.py` checks every repository path cited in a Markdown code span
against the filesystem. `.github/scripts/check-workflows.py` checks what a YAML parse does not — a
dangling `needs:`, an action pinned to a mutable tag instead of a SHA, a missing `permissions:`, a
`$VAR` defined in no `env:` block, a script named in a `run:` but absent from disk. Both were
developed against deliberately faulted inputs, because a validator whose clean result has never been
contrasted with a dirty one is not evidence of anything.

### `TreatWarningsAsErrors` is not negotiable

It is set in `Directory.Build.props` and applies to every project. Do not override it from the
command line, and do not add `NoWarn`.

**You may not make the build pass by silencing the compiler.** No `#pragma warning disable`, no
`<NoWarn>`, no lowering a severity in `.editorconfig`. If a diagnostic is genuinely wrong for one
specific place, add a **path-scoped** `.editorconfig` section for that file with a comment saying
why. Every existing suppression follows that rule and you can read the reasoning next to it.

Be aware that those suppressions are **indexed by path**: move a file and its suppression silently
stops applying, and the build breaks. That is intended — it forces the exemption to be re-justified
at its new home.

### `.cs` files must be UTF-8 **with BOM**

`.editorconfig` sets `charset = utf-8-bom`. A file created without the BOM fails
`dotnet format --verify-no-changes` on encoding alone, with a message that does not obviously say so.
After creating or moving any file:

```bash
./tasks.ps1 format-fix     # i.e. dotnet format AppTemplate.sln
```

## Layout

Four layers, and each one has the same shape, so that changing layer does not mean learning a new
filing system:

```
<Layer>/<Project>/
  Common/<Responsibility>/
  Features/<Feature>/<Responsibility>/
```

```
AppTemplate.Domain/         Common/{Abstractions,Events,Exceptions,Primitives}
                   Features/<F>/{Entities,Events,ValueObjects,Repositories}
AppTemplate.Application/    Common/{Abstractions,Validation,Idempotency,Collections,Concurrency}
                   Features/<F>/{UseCases/{Commands,Queries}/<Operation>,Ports/<Port>,
                                 Consumers,Services,Policies,Extensions,Mapping,Dtos,Errors}
AppTemplate.Infrastructure.Persistence/
                   Common/{Contexts,Idempotency,Saving/{Auditing,DomainEvents,Tracking},Time}
                   Features/<F>/{Models,Configurations,Mapping,Tracking,Repositories,Queries,
                                 Observability,Seeding,Tables}
                   Migrations/
AppTemplate.Api/            Common/{Caching,Concurrency,Contracts,Controllers,Errors,Hosting,
                                    Idempotency,Observability,OpenApi,Security}
                   Features/<F>/{Controllers,Contracts/{Requests,Responses},Mapping}
AppTemplate.Worker/         Common/{Observability,Security}
                   Features/<F>/            one BackgroundService, its options, its metrics
AppTemplate.Infrastructure.Email/       Common/{Smtp}            Features/<F>/
AppTemplate.Infrastructure.InMemory/    Common/{Email,Time}      Features/<F>/
AppTemplate.Infrastructure.Identity/    by subject: Accounts, AccessTokens, RefreshTokens,
                                        EmailConfirmation, EmailChange, PasswordReset,
                                        TwoFactor, SecurityEvents
Tests/             a 1:1 mirror of Src/
```

The vocabulary under `Features/<F>/` is closed, and
`LayoutConventionTests.EveryFeatureFolder_IsNamedFromItsLayersVocabulary` holds it for all seven
projects above that have a `Features/` directory — including `AppTemplate.Worker`, whose list is deliberately empty: its features
hold their files side by side with no subfolder, so the first subfolder anyone adds has to be
argued for in the pull request that adds it, and written into this file, rather than created
quietly.

**The first level of `Common/` is closed too**, per project, held by
`EveryCommonFolder_IsNamedFromItsProjectsVocabulary` — and it is the newer of the two rules for a
reason: nothing checked `Common/` in any layer, and that is where every layout defect of the last
month appeared. Only the first level is checked; how a folder partitions itself below that is its
own business, which is why `Saving/` may hold `Auditing/`, `DomainEvents/` and `Tracking/`.

A folder is only present when it has content: a feature with no read-side projection has no
`Queries/`, one with no domain-event consumer has no `Consumers/`. `UseCases/{Commands,Queries}/`
is one folder per operation — the command or query record, its named interface, the use case, and
its FluentValidation validator live together, because they are that one operation's signature.
`Dtos/` holds only the read models more than one operation shares; a shape only one operation
returns lives beside that operation instead. `Ports/<Port>/` holds a port's interface together
with the messages that cross it, and a type in a port's signature never moves into a use case's
own folder, however many use cases happen to call it — otherwise `Ports/` would depend on
`UseCases/`.

Rules that the architecture tests enforce, so you will find out anyway:

- **A folder even for a single file.** No `.cs` at a project root except the `.csproj`, the DI module
  class, and `Program.cs`.
- **No `Services/`, `Interfaces/`, `DTOs/`, `Helpers/`, `Managers/` at a project root.** Sorting by
  technical type is banned; it is allowed only *inside* a feature.
- **An infrastructure module takes `Common/` + `Features/` when it has both kinds of adapter**, and
  names its folders after subjects when it does not. `Email` and `InMemory` have a transverse
  adapter (`IEmailSender`) and a feature-scoped one (`IReminderNotifier`), so the tree says which
  files leave with the example feature. `Identity` serves one feature entirely, so `Features/Auth/`
  would hold every file it has and `Common/` would be empty; its folders name subjects instead.
- **Namespaces follow folders.** No exceptions.
- `Tests/` mirrors `Src/` one directory for one directory.

`Features/` is the unit of vertical slicing. The word *module* is reserved for DI composition
(`AddPersistenceModule`) — that is a container concept, not a business boundary.

## Design conventions

**Interfaces.** Every service class has one; code depends on the interface and DI supplies the
implementation. An interface with a single implementation is fine here — that is a deliberate
testability choice, not an oversight. Default visibility is `internal sealed`; only ports,
configuration-bound options classes, and types the host must name are `public`.

**Where a contract lives.** A repository contract goes in `AppTemplate.Domain/Features/<F>/Repositories/`,
because it speaks only in domain types. Every other port goes in `AppTemplate.Application`, because it speaks
in DTOs or platform concerns. `AdapterVisibilityTests` enforces this by recognising a repository contract from
a namespace ending in `.Repositories`.

**Use cases.** One class per use case, plus **one named interface per use case** inheriting
`IUseCase<TRequest,TResponse>` (or `IUseCase<TResponse>`) — the named interface is what constrains the
signature and what the controller depends on. Registration is automatic, by type identity, over a
materialised set, and the container **throws at startup** if a use case declares zero or several
named interfaces. Never write `if (x != null)` without an `else`.

**Failures.** `Result`/`Result<T>` carrying an `Error` with a stable machine-readable `code` for
expected outcomes. `DomainException` only for a violated invariant. `ConcurrencyConflictException`
only for a lost update. Everything becomes an RFC 7807 `ProblemDetails` with that `code`, and
**no exception message ever reaches a client**.

**Persistence.** EF maps persistence models (`*Record`), never the domain entities. A mapper, a
tracker (identity map) and a flush interceptor assign domain state onto tracked rows, and EF computes
the delta. The concurrency token is PostgreSQL's `xmin` and it round-trips in both directions. Domain
events are drained from the tracker and published after a successful commit, exactly once. If you are
changing any of that, add tests for the *update* path, not just the insert path: an insert that
works proves nothing about a delta EF computed from a tracked row.

## Naming

**A type's name says what it is, not only what it is about.** Reading the file name alone has to
answer "what nature of thing is this?", so the suffix is not decoration:

| Suffix | What it names |
|---|---|
| `…UseCase`, `…Command`, `…Query` | one operation, its input, and its implementation |
| `…Outcome` | the record an operation hands back — a use case's or a port's |
| `…Status` | the closed enum of ways it went, carried by an `…Outcome` |
| `…Repository`, `…Store`, `…Queries` | an aggregate, storage with no aggregate, a read-side projection |
| `…Service` | an injected implementation that has dependencies |
| `…Mapper` / `…Mapping` | a mapping, injected / static — and named for what comes **out** of it |
| `…Extensions` | a static class of `AddX`/`UseX` composition methods |
| `…Options`, `…Validator`, `…Policy`, `…Consumer`, `…Dto`, `…Controller`, `…Request`, `…Response` | as they read |

Two rules follow from having been broken:

- **A word names one notion in this repository.** `Outcome` once named both a use case's return
  record and the enum inside a port's, and `Policies` named both a business rule under
  `Features/<F>/Policies/` and the file holding an `AddApiX`. When a second meaning appears, one of
  the two is wrong — find which and rename it, do not document the ambiguity.
- **The port carries the nature word, and the adapter is the port without its `I`.**
  `IUserAccountsService` is implemented by `UserAccountsService`; `ISecurityEventLog` by
  `SecurityEventLog`, because `Log` already says what it is. The one exception is a port several
  modules implement, where a qualifying prefix tells them apart: `MailKitEmailSender` and
  `InMemoryEmailSender` both satisfy `IEmailSender`.
  A port's *folder* keeps the capability name — `Ports/UserAccounts/IUserAccountsService.cs` — and
  that is not cosmetic: a folder named for the interface would put a namespace and a class of the
  same name in scope of each other, which is CS0118 at every consumer.

Banned outright: `Manager`, `Helper`, `Utils`, `Processor`, a bare `Handler`, and `Composer`. Each
of them names "code" rather than a thing, and each attracts whatever nobody could classify. There is
no `Utils/` folder in this repository and there is not meant to be one; needing it means a name is
missing, not a folder.

## Comments

Minimal, short, and about *why*. The test is: **if I delete this comment, can someone introduce a
bug?** If not, it goes.

Specifically banned: comments that paraphrase the code, and comments that narrate the repository's
own history ("this used to…", "the old implementation…", "fixed the bug where…"). Git holds that.
XML doc only when it says something the signature does not.

## Tests

xunit.v3 + Shouldly + NSubstitute. **Not FluentAssertions** — its licence changed and it is not
coming back.

- `TestContext.Current.CancellationToken` in async calls. xUnit1051 is an error here.
- `Arg.Any<CancellationToken>()` for NSubstitute placeholders, never `default`.
- The tree mirrors `Src/` exactly.

**A test that buys a guarantee must be able to fail.** For anything about security or correctness:
break the production code, *watch the test go red*, restore it, and check the failure named what you
thought it named. A green test you never saw fail is not evidence. Rules follow from experience in
this repository:

- An architecture rule can pass **vacuously** on an empty candidate set. Assert the set is non-empty
  *before* asserting the condition. `AggregateRoots_AreSealed` once matched zero types and passed.
- A test project missing from `AppTemplate.sln` makes `dotnet test AppTemplate.sln` green with zero tests. CI asserts
  every project under `Tests/` is in the solution, and that the run executed more than zero tests.
- **A test that picks its own static type does not test the real path.** A unit test asserting the
  discriminator of a polymorphic response passed while the API served a body without one, because it
  called `JsonSerializer.Serialize<TBase>(…)` and named the base itself. MVC does not: `Ok(value)`
  leaves `DeclaredType` null and the formatter serialises the runtime type. When a guarantee depends
  on the framework, prove it through the framework — read the raw response.
- **A comment claiming a guarantee is covered elsewhere is a claim to check, not evidence.** Several
  have been wrong here: an ETag asserted to be replayed that was not, a cross-reference naming the
  wrong layer, a formatting gate believed to catch unused usings that did not.
- **Some defects only an end-to-end test can see.** An exhaustive `switch` over an event enum threw
  on a newly added member and turned the first real call into a 500; every unit test around it had
  substituted that collaborator away. Where a component is substituted everywhere, something has to
  exercise the real one.
- **Never compare two problem documents whole.** They carry a `traceId`, which identifies the
  request rather than its outcome, so two requests always differ — an assertion that they are equal
  is asserting that two requests are the same one. Compare everything else.

### Known sharp edge: coverage and the architecture tests

NetArchTest resolves each type through `Type.GetType(name, throwOnError: true)`, and that fails
against a Coverlet-instrumented assembly. Running `AppTemplate.Architecture.Tests` under
`--collect:"XPlat Code Coverage"` makes its NetArchTest-based rules throw; without the collector the
project passes whole. So coverage is collected over every project **except** that one, in CI and in
`./tasks.ps1 coverage` alike. Do not merge those runs back together.

### Sharp edges worth knowing before they cost you an afternoon

Each of these has been paid for at least once in this repository.

- **`Result<TValue>.Value` throws on a failure.** So `is { IsSuccess: true, Value: var x }` evaluates
  the getter *while* matching and throws instead of not matching. Test `IsFailure` first.
- **FluentValidation runs the remaining rules for a property even after `NotNull()` failed.** A
  `Must` that dereferences needs `.Cascade(CascadeMode.Stop)` ahead of it.
- **NSubstitute's `Arg.Is<T>` takes an expression tree**, which rejects pattern matching (CS8122).
  Compare a record by value instead of by predicate.
- **The injectable clock controls neither JWT validation nor ASP.NET Identity**, both of which read
  `TimeProvider.System`. Moving it forward and then signing in mints a token whose `nbf` is in the
  future, refused as `IDX10222` — not an expired one. It has no effect on lockout end dates or on
  confirmation and reset token lifetimes either.
- **The rate limiter's window advances on wall-clock time** and exposes no injectable clock;
  `AutoReplenishment = false` does not change that. The window itself is replaceable
  (`RateLimiterWindow`) so a test host can widen it.
- **Never name a folder after the type it contains.** A namespace and a type sharing a name make
  name resolution ambiguous for consumers (CS0118), because lookup walks the enclosing namespaces.
- **`.cs` files are UTF-8 *with* BOM** (`.editorconfig`), and a file written without one fails the
  formatting gate. Detecting a BOM by piping bytes through `grep` does not work reliably — it will
  happily add a second one.
- **A stale `<see cref="…"/>` and an unused `using` both fail the build**, because
  `GenerateDocumentationFile` is on with CS1591/CS1573 suppressed. Documentation stays optional;
  what is written has to be true.

## Adding a feature

The vertical, from the inside out. `TodoLists` is the worked example — read it alongside this list.
`Reminders` is the second one, and the more useful comparison for a new feature: a flat aggregate
with no child entities, so what a to-do list needs only because it owns items is visible by what
a reminder does without.
[docs/ADDING-A-FEATURE.md](docs/ADDING-A-FEATURE.md) walks the same vertical with the actual
signatures at each step, if this checklist is not enough on its own.

1. **Domain** — `AppTemplate.Domain/Features/<F>/`: the aggregate root in `Entities/`, value objects in
   `ValueObjects/`, events in `Events/`, and the repository contract in `Repositories/`. Invariants
   belong in the constructor, the factory, and `Rehydrate` — all three, or a stored row can produce
   an aggregate that breaks its own rules.
2. **Application** — `AppTemplate.Application/Features/<F>/`: one folder per operation under
   `UseCases/{Commands,Queries}/<Operation>/`, holding the command or query record, its named
   interface, the use case, and its FluentValidation validator together. Any port that is not the
   repository goes in `Ports/<Port>/`, next to the messages that cross it. Read models more than one
   operation shares go in `Dtos/`; the feature's failure vocabulary goes in `Errors/`. Validate
   against the *trimmed* value if the domain normalises.
3. **Persistence** — `AppTemplate.Infrastructure.Persistence/Features/<F>/`: the `*Record` in `Models/`, its
   `IEntityTypeConfiguration` in `Configurations/`, the mapper in `Mapping/`, the tracker in
   `Tracking/`, the repository implementation in `Repositories/`, read-side projections in `Queries/`.
   Register them in `PersistenceModule`. **A tracker must resolve as one instance under every
   contract it serves** — three independent registrations give three instances, and every write then
   persists nothing, silently. `SharedInstanceRegistrationTests` is the guard.
4. **API** — `AppTemplate.Api/Features/<F>/`: the controller in `Controllers/`, request records in
   `Contracts/Requests/`, response records in `Contracts/Responses/`, and the mapping between them
   and the application's DTOs in `Mapping/`. Endpoints are authenticated by default; opting out needs
   an explicit `[AllowAnonymous]`.
5. **Tests** — mirror each of the above.
6. **Migration** — `./tasks.ps1 migration-add <Name>`, then confirm
   `has-pending-model-changes` reports nothing.

## Migrations

Generate with `./tasks.ps1 migration-add <Name>`. The application applies migrations at startup
**only in Development**; a deployment applies them as a separate step from a bundle
(`./tasks.ps1 migration-bundle`). Applying them at startup in production would give the
application's own runtime credentials the right to alter the schema, and would race every replica
against every other on a rolling deploy; `SECURITY.md` says what a deployment still owes.

There are two, and the split is deliberate: `InitialCreate` carries the `identity` and `platform`
schemas — what every project keeps — and `AddExampleFeatures` carries `todo` and `reminders` alone,
so removing the examples is a deleted file rather than a drop migration. A new migration goes on the
end as usual; the split only matters for the two that are there.

**If `dotnet ef` refuses to run**, failing on `Settings file 'DotnetToolSettings.xml' was not found
in the package`, the local tool manifest is what is broken, not the package. A globally installed
copy invoked by its full path works: `dotnet tool install --global dotnet-ef` then
`$HOME/.dotnet/tools/dotnet-ef migrations add <Name> --project …`.

Whatever produced them, `PendingModelChangesTests` is what proves a migration matches the model —
it calls `HasPendingModelChanges()` and needs no database — and the integration suites are the only
thing that ever executes an `Up()`.

## Decisions already made, and the shape they impose

These are the choices a reasonable person could have made differently. They are written here, and
where a test can hold one it does — a rule nothing verifies is re-derived and lost inside six
months. If one of them is wrong for your project, change it deliberately: change the sentence here
*and* the test that holds it, in the same commit.

**Writes are named operations. There is no `PATCH`.** A partial update whose semantics depend on
which keys the client happened to send cannot be validated against an invariant, because the
invariant is a property of the whole aggregate. So an omitted field means *absent*, not *unchanged*,
and every write says in its URL what it does. `NoEndpoint_AcceptsPatch` holds it.

**Filtering is a closed set of typed parameters.** Not a filter expression language: a grammar the
client composes is a query planner you now own, an injection surface, and a performance cliff no
index can flatten. Adding a filter means adding a parameter and a test, which is the point.

**Pagination metadata is in the body.** `PagedResult<TItem>` says whether there is a next page; no
`Link` header duplicates it. One statement of the fact, in the place every client already parses.
`NoResponse_CarriesALinkHeader` holds it.

**No `Deprecation` or `Sunset` headers while one version ships.** They would announce a schedule
nobody has committed to. `NoResponse_AnnouncesItsOwnDeprecation` holds it; delete it the day a
second version exists.

**No soft delete.** `DELETE` removes rows. A deleted flag makes every query carry a predicate that
one forgotten `Where` turns into a data leak, and it answers a retention question the audit trail
should answer instead. `NoPersistenceRecord_CarriesADeletedFlag` holds it.

**Correctness does not depend on event delivery.** Domain events are dispatched in-process, after
commit, at most once, with no outbox — so a consumer may simply not run. That is survivable only
because no consumer is the *only* thing keeping a rule true: the effect re-derives its precondition
when it runs, so running twice is the same as running once, and never running leaves the system
consistent but stale. A counter makes the divergence observable
(`apptemplate.reminders.missed_cancellations`, watched per `SECURITY.md`). **Any consumer you add
has to have that shape**, or the missing outbox stops being a cost and becomes a bug.

**Extract what two real cases prove identical, and nothing more.** A guessed abstraction is a worse
defect than an assumed duplication: the duplication is visible and local, the wrong abstraction is
neither. Measure first — `wc -l`, `diff` — and require two cases that do the same thing, not two
that resemble each other. `AggregateTracker` is what that looked like when it succeeded: seven
members that read nothing but what `IVersioned`, `IAuditable` and `AggregateRoot<Guid>` already
name. `FlushTo` stayed abstract, and the two repositories were never touched.

**Authorisation is default-deny.** The fallback policy requires an authenticated user, so a new
endpoint is closed until someone opens it. `[AllowAnonymous]` is the visible exception and it is
whitelisted by name in `DefaultDenyTests`; adding one is a line in that test.

**Four words name the four ways this template reaches storage, and each one is checked.**
`Repository` loads an aggregate, so its contract lives in `AppTemplate.Domain` beside the aggregate.
`Queries` projects rows onto a DTO without materialising one. `Store` is an application port for
storage with no aggregate behind it — `IIdempotencyStore`, which a use case depends on. `Table` is
row access to one table, declared inside the persistence project and reached only by a sibling
infrastructure module — `IRefreshTokenTable`, which no use case has ever heard of.
`StorageVocabularyTests` holds all four: a `Store` or a `Table` naming a domain entity has become a
`Repository`, a `Repository` declared outside the Domain is a promise it cannot keep, and a `Table`
a use case depends on has become a port and needs declaring where ports are.
The four-operation ceiling `PortConventionTests` enforces is a rule about **ports** — the façade a
use case sees. A `Table` is not one, and `IRefreshTokenTable` has six operations deliberately: it is
one table's whole surface, held narrow by having exactly one consumer rather than by a count.

**No MediatR, no dispatcher, no pipeline behaviours.** A controller names the use case it calls, and
`F12` reaches the implementation. `LayerDependencyTests` forbids the package by name.

**A port never exposes `IQueryable`.** A contract that hands out a query tree has not abstracted the
database, it has published it — and every caller becomes a place where a lazy load can happen.
`NoApplicationPort_ExposesAQueryable` holds it.

## Pull requests

- One concern per PR. A refactor and a behaviour change in one diff cannot be reviewed.
- The gate above passes, without overrides.
- New behaviour comes with a test you have seen fail.
- A new architectural constraint you checked by hand becomes an executable rule in
  `Tests/Architecture/`, or it will be re-derived and lost.

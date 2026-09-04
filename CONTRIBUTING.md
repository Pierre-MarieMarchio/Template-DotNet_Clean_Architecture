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
                   Common/{Contexts,Auditing,DomainEvents,Mapping,Time,UnitOfWork,Idempotency}
                   Features/<F>/{Models,Configurations,Mapping,Tracking,Repositories,Queries}
                   Migrations/
AppTemplate.Api/            Common/{Controllers,Contracts,Errors,Http,Idempotency,Caching,
                                    Security,Startup,OpenApi,Lifecycle,Observability,Concurrency}
                   Features/<F>/{Controllers,Contracts/{Requests,Responses},Mapping}
Tests/             a 1:1 mirror of Src/
```

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
  technical type is banned; it is allowed only *inside* a feature. The one admitted exception is an
  infrastructure module project, where the project itself is the feature boundary — so its first
  level counts as "inside a feature".
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
changing any of that, read `docs/adr/0011` first and add tests for the *update* path, not just the
insert path.

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
thought it named. A green test you never saw fail is not evidence. Two rules follow from experience
in this repository:

- An architecture rule can pass **vacuously** on an empty candidate set. Assert the set is non-empty
  *before* asserting the condition. `AggregateRoots_AreSealed` once matched zero types and passed.
- A test project missing from `AppTemplate.sln` makes `dotnet test AppTemplate.sln` green with zero tests. CI asserts
  every project under `Tests/` is in the solution, and that the run executed more than zero tests.

### Known sharp edge: coverage and the architecture tests

NetArchTest resolves each type through `Type.GetType(name, throwOnError: true)`, and that fails
against a Coverlet-instrumented assembly. Running `AppTemplate.Architecture.Tests` under
`--collect:"XPlat Code Coverage"` makes 7 of its 40 tests throw; without the collector all 40 pass.
So coverage is collected over every project **except** that one, in CI and in `./tasks.ps1 coverage`
alike. Do not merge those runs back together.

## Adding a feature

The vertical, from the inside out. `TodoLists` is the worked example — read it alongside this list.
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
(`./tasks.ps1 migration-bundle`). See `docs/adr/0009` for why, and `SECURITY.md` for what a
deployment still owes.

## Architecture decisions

`docs/adr/` holds one record per decision a reasonable person could have made differently, including
the rejected options — usually the useful part. If one of them is wrong for your project, **supersede
it with a new numbered record** rather than editing history; `0006` shows the shape.

## Pull requests

- One concern per PR. A refactor and a behaviour change in one diff cannot be reviewed.
- The gate above passes, without overrides.
- New behaviour comes with a test you have seen fail.
- A new architectural constraint you checked by hand becomes an executable rule in
  `Tests/Architecture/`, or it will be re-derived and lost.

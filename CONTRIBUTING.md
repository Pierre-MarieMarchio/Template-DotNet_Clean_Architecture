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
dotnet run Tools/Tasks.cs restore
dotnet run Tools/Tasks.cs build
dotnet run Tools/Tasks.cs test                   # everything
dotnet run Tools/Tasks.cs test --no-integration  # skips the Testcontainers suite
dotnet run Tools/Tasks.cs hygiene                # doc paths, workflow structure, comment tense
dotnet run Tools/Tasks.cs verify                 # the whole gate, in CI's order
```

Everything under `Tools/` is a file-based C# app, which is the whole reason the prerequisite list
below has one entry. `dotnet run Tools/Tasks.cs <task>` is the same line on Windows, Linux and
macOS, in whichever shell you use, and it needs nothing installed that building the solution does
not already need. There is no interpreter to locate, no line-ending rule to preserve, and no
package to fetch before a gate will start.

The first run of a given file compiles it — a second or two on a warm machine, a few on a cold one
— and every run after that is served from the build cache in about a third of a second. A first
invocation that pauses is the compiler, not a hang.

`hygiene` costs the SDK, like every other task here. That is the deliberate trade: it asks for no
second language runtime and no package-manager step ahead of it, so the machine that can build this
repository can also judge it, straight after a clone.

Prerequisites: the .NET SDK version pinned in `global.json` (nothing else — `rollForward` is
`latestFeature`, so a newer patch of the same feature band is fine), and Docker for the integration
tests. `dotnet run Tools/Tasks.cs compose-up` starts the whole stack — PostgreSQL, mailpit, MinIO
and its bucket, the API and the Worker — and waits until each one is healthy. For the "run the app
by hand" path you only need the backing services: `docker compose up -d db mailpit minio
minio-bucket`.

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
dotnet run Tools/Tasks.cs coverage         # line coverage >= coverage.minimum
dotnet run Tools/Tasks.cs hygiene          # doc paths resolve, workflows are sound
```

The last one exists because two classes of defect are invisible to the compiler and to the tests:
a documented path that resolves to nothing, and a workflow that would fail the first time it ran.
`Tools/CheckDocPaths.cs` checks every repository path cited in a Markdown code span against the
filesystem. `Tools/CheckWorkflows.cs` checks what a YAML parse does not — a dangling `needs:`, an
action pinned to a mutable tag instead of a SHA, a missing `permissions:`, a `$VAR` defined in no
`env:` block, a script named in a `run:` but absent from disk. `Tools/CoverageGate.cs` reads the
Cobertura reports against the floor in `coverage.minimum`, and `Tools/CheckNarrativeComments.cs`
holds the comment rule below.

### A gate proves it can go red before it judges anything

Each of the four takes a `--self-test` flag, and running it is a step in its own right rather than
a nicety:

```bash
dotnet run Tools/CheckDocPaths.cs --self-test
dotnet run Tools/CheckWorkflows.cs --self-test
dotnet run Tools/CoverageGate.cs --self-test
dotnet run Tools/CheckNarrativeComments.cs --self-test
```

Each carries two sets of fixtures, built in a temporary tree and thrown away. The faulted ones —
each labelled with the defect it stands for — **must** make the gate fire, and the sound ones
**must** leave it silent, which is the half that catches a rule grown eager enough to flag a
legitimate line. A gate whose green has never been contrasted with a red says nothing about the
repository; it says only that it ran.

`hygiene` and `verify` run every self-test before letting the corresponding gate look at the tree,
and `.github/workflows/ci.yml` does the same, one step per self-test so a fixture failure is
legible on its own rather than buried in the gate that follows it. Adding a rule to a gate means
adding the fixture that fails without it.

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
dotnet run Tools/Tasks.cs format-fix     # i.e. dotnet format AppTemplate.sln
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
AppTemplate.Domain.Core/    Common/{Abstractions,Events,Exceptions,Primitives}
                   no Features/: a primitive belongs to no feature
AppTemplate.Domain/         Common/{Tagging}
                   Features/<F>/{Entities,Events,ValueObjects,Repositories}
                   Common/ is the business-shared half: what two features share
                   *as business*. The primitives are AppTemplate.Domain.Core's
AppTemplate.Application.Core/
                   Common/{Collections,Concurrency,Events,Idempotency,Localization,
                           Policies,Ports,Results,UseCases,Validation}
                   Features/Maintenance/UseCases/Commands/<Operation>
AppTemplate.Application/    Common/{Tagging}
                   Features/<F>/{UseCases/{Commands,Queries}/<Operation>,Ports/<Port>,
                                 Consumers,Services,Policies,Extensions,Mapping,Dtos,Errors}
                   Common/ is the business-shared half; the mechanisms that know
                   no feature are AppTemplate.Application.Core's
AppTemplate.Application.Auth/
                   Features/Auth/{Errors,Policies,Ports/<Port>,
                                  UseCases/{Commands,Queries}/<Operation>}
                   one feature, so nothing is shared between features here
AppTemplate.Infrastructure.Core/
                   Common/{Caching,Templating}
                   no Features/: a mechanism a module needs and no module owns
AppTemplate.Infrastructure.Persistence/
                   Common/{Contexts,Idempotency,Leases,Options,Saving/{Auditing,DomainEvents,Tracking},Time}
                   Features/<F>/{Models,Configurations,Mapping,Tracking,Repositories,Queries,
                                 Observability,Seeding,Tables}
                   Migrations/
AppTemplate.Presentation.Core/
                   Common/{Jobs,Localization,Observability,Outbound,Security}
                   no Features/: what any host needs whatever its transport belongs
                   to none of them
AppTemplate.Api.Core/       Common/{Caching,Concurrency,Contracts,Controllers,Errors,Hosting,
                                    Idempotency,Localization,Observability,OpenApi,Security}
                   no Features/: the half of an HTTP host that knows no feature
AppTemplate.Api/            Common/{Hosting,Observability,Security}
                   Features/<F>/{Controllers,Contracts/{Requests,Responses},Mapping}
                   Common/ is a third kind: what this host cannot delegate — what
                   names a concrete infrastructure module, or decides a deployment policy
AppTemplate.Worker/         Common/{Observability,Security}
                   Features/<F>/            one BackgroundService, its options, its metrics
AppTemplate.Infrastructure.Email/       Common/{Http,Smtp}       Features/<F>/
AppTemplate.Infrastructure.InMemory/    Common/{Email,Time}      Features/<F>/
AppTemplate.Infrastructure.Identity/    Common/{Directories,Options}
                   Features/Auth/{Directories,Factories,Issuers,Logs,Options,Providers,
                                  Services,Templates,Verifiers}
AppTemplate.Infrastructure.Storage/     Common/{Budgets,Factories,Options}
                   Features/Files/{Inspectors,Inventories,Options,Scanners,Stores}
Tests/             a 1:1 mirror of Src/
```

**Two different `Common/` folders, and the tree above holds both.** `<Layer>.Core/Common/` is the
half of a layer that is agnostic of the business: `Result`, `Error`, `PageRequest`, `SortOrder`,
`IUnitOfWork`, `ICurrentUser`, `AggregateRoot<TId>`, `IDomainEvent`. Nothing in it knows a feature,
and nothing in it ever may — that is the whole claim of those projects. `<Layer>/Common/` is the
business-shared half: what several features share *as business*, a value object three features
spend, a DTO two features answer with, a policy that spans them. It exists so that business code is
not repeated across features, and it belongs in the business project rather than in the Core.

**The sorting test is one question: does it know a feature?** If it names a feature — or would have
to, the moment a second feature used it — it belongs in the business project's `Common/`. If it does
not and never will, it belongs one project inwards.

`Common/Tagging/` is the worked example of the business half, in both layers. A to-do item and a
stored file are both tagged, and they are tagged by the same rule — a tag is held once however many
times it is sent, a cap bounds how many one thing carries, and a replacement is total rather than a
merge — so `TagSet` states it once and both aggregates own one. **The test that put it there rather
than one project inwards is mechanical, not a matter of taste: `TagSet` names `Tag`, and a `.Core`
project may name no business type.** Its generic shape — a bounded, de-duplicating collection —
would have been agnostic; what it actually is is not. The application half is the same argument one
layer up: `TagValidation` restates the domain's bounds for a caller, reads the domain's constants
rather than literals, and names `Tag`.

What is *not* there is as informative. The ownership check every feature performs, and the paginated
read every feature exposes, are both duplicated and both agnostic — they name no feature and never
would — so they belong one project inwards and not here. A folder in the business `Common/` is
earned by naming a business type, not by being repeated.

`AppTemplate.Domain.Core` is the innermost project of the tree, and it holds the primitives an
aggregate is built from: an entity, an aggregate root, a domain event, the audit and concurrency
contracts a persistence row opts into, and the one domain exception. It is written as a package and
vendored rather than published, and every part of that shape is load-bearing. It references
nothing at all — no project, no runtime package — because a dependency taken by the innermost
project is a dependency taken by everything above it, and an architecture rule holds that. Its
public surface is tracked in `Src/Domain/AppTemplate.Domain.Core/PublicAPI.Shipped.txt` and
`Src/Domain/AppTemplate.Domain.Core/PublicAPI.Unshipped.txt`, so widening it is an explicit diff to
review rather than a side effect of an edit nobody read as one. `CS1591` is re-enabled there and
nowhere else, because a derived project reads those signatures instead of reading the code, so
every public member owes it a sentence. It carries no version: a project that needs a primitive
this one does not have copies the project and tunes it. And `dotnet pack` runs over it, which is
what actually proves the self-containment — packing fails the moment it acquires a reference to
something unpackable. What all of that buys is the division the tree above shows: a project derived
from this template writes its own `Features/`, plus whatever `Common/` its own features come to
share as business — and never a primitive.

`AppTemplate.Application.Core` is the same argument one layer out, and it holds the mechanisms a
use case is written *from* rather than any use case's business: `Result` and `Error`, the `IUseCase`
marker every named interface derives from, validation, offset and cursor pagination, idempotency,
optimistic concurrency, and the cross-cutting ports a host has to satisfy — the clock, the caller's
identity, the commit boundary, the mail relay, the leader lease. It references
`AppTemplate.Domain.Core` and nothing else, never `AppTemplate.Domain`: the two files here that
need a domain type — `IDomainEventConsumer` and `DomainGuard` — need only the primitives, so this
project knows no aggregate and cannot come to know one. Its public surface is tracked in
`Src/Application/AppTemplate.Application.Core/PublicAPI.Shipped.txt` and
`Src/Application/AppTemplate.Application.Core/PublicAPI.Unshipped.txt`, `CS1591` is re-enabled here
too so every public member carries a sentence, `dotnet pack` runs over it, and it carries no
version, for the same reasons as the project inwards. The one use case it holds,
`Features/Maintenance/UseCases/Commands/PurgeExpiredIdempotencyKeys/`, is here because it is the
idempotency mechanism's own housekeeping and names no business type; it is registered by
`AddPurgeExpiredIdempotencyKeys()` a call at a time, so a host with neither a maintenance endpoint
nor a maintenance loop is not made to supply the two ports it resolves. There is deliberately no
`AddApplicationCore()`: what this project declares is ports, results and markers, which nothing
registers, and an umbrella call that registered nothing would read like the seam that makes the
project work.

`AppTemplate.Application.Auth` is authentication and account administration as application-layer use
cases — sign-in, refresh-token rotation, the password and email lifecycle, two-factor enrolment,
external providers, roles and lockouts — behind the twenty ports under `Features/Auth/Ports/`. It
references `AppTemplate.Application.Core` and nothing else, and it names no domain type at all: the
identity model is not an aggregate here, it lives behind those ports. It is the project a derived
application is most likely to replace wholesale with its own identity provider, which is what earns
it a project of its own, and one call — `AddAuthApplication()` — that a host either makes or does
not. It is written package-grade like the two `.Core` projects, with the same tracked public surface
in `Src/Application/AppTemplate.Application.Auth/PublicAPI.Shipped.txt` and
`Src/Application/AppTemplate.Application.Auth/PublicAPI.Unshipped.txt`, `CS1591` re-enabled, `dotnet
pack` over it and no version. Dropping it is not free, and the cost is not in this project: see
`docs/REMOVING-THE-EXAMPLE-FEATURES.md` for the three places that answer for it.

`AppTemplate.Presentation.Core` is the presentation layer's agnostic half: what any host needs
whatever its transport, and nothing that names one. Five subjects, one file each. An outbound HTTP
policy installed on `IHttpClientFactory`'s defaults. `LocalizationOptions`, the language a flow is
written in when nobody said which one to read. `TelemetryOptions` and the OTLP tracer and meter over
it. `NoCallerCurrentUser`, the answer a process with no request gives to "who is calling" —
which throws rather than returning `null`, because `null` there is indistinguishable from a
legitimately anonymous caller. And `PeriodicJob`, the loop recurring work is written on: one
interval, one asynchronous iteration, a primitive a service composes rather than a base class a
service derives from, which is what lets one host's loop run three passes on three intervals
without three classes. It references `AppTemplate.Application.Core` and nothing else, and it
is written package-grade like the three projects inwards: a tracked public surface in
`Src/Presentation/AppTemplate.Presentation.Core/PublicAPI.Shipped.txt` and
`Src/Presentation/AppTemplate.Presentation.Core/PublicAPI.Unshipped.txt`, `CS1591` re-enabled so
every public member owes a sentence to the reader who reads signatures instead of code, `dotnet
pack` over it, and no version — it is vendored, and a project that needs a subject this one lacks
copies it and tunes it.

**It carries no `FrameworkReference`, and that absence is the whole reason it is a project of its
own rather than half of one presentation Core.** A host with no HTTP surface — a desktop client, a
CLI — gets a clock, a culture and an outbound budget without inheriting ASP.NET, which is what keeps
that door open at all. It is also why four packages this repository uses are deliberately absent
here: each carries the ASP.NET framework reference in its own nuspec, so referencing one would pull
the shared framework in through the back door. `Scalar.AspNetCore`,
`Microsoft.AspNetCore.OpenApi`, `OpenTelemetry.Instrumentation.AspNetCore`, and `Asp.Versioning.Mvc`
through its dependency `Asp.Versioning.Http` — whose own nuspec is clean, which is what makes that
one hard to see. `Npgsql.OpenTelemetry` is absent for a different reason: it pulls the PostgreSQL
driver, and a presentation SDK that carried one would put a database behind every host, desktop ones
included. Each host keeps that package and adds `.AddNpgsql()` through the tracing callback
`AddObservability` offers, which is one of three things the shared registration takes from the host
rather than deciding for it — see `docs/ARCHITECTURE.md` for all three and why each has to be that
way.

`AppTemplate.Api.Core` is the same division one project further out, for the transport
`AppTemplate.Presentation.Core` refuses to know: the half of an HTTP host that knows no feature.
Eleven `Common/` folders and one file at its root — problem details with error codes, ETag and
`If-Match`, the idempotency filter, rate limiting, CORS, security headers, forwarded headers,
request timeouts and size limits, API versioning, the health endpoints, and the request-language
middleware. It is `Microsoft.NET.Sdk` plus a `FrameworkReference` rather than the web SDK, which
costs it nine implicit `using` directives and buys the guarantee that what needs ASP.NET is exactly
what is in here. **Public is exactly what a host names, and the compiler settled it type by type:**
the registration extensions are internal, because a public `Add` with no matching `Use` is a seam
nobody can use, and `UseCorePipeline()` is monolithic on purpose — the ordering constraints inside
it are coupled pairwise, so there is no safe extension point in the middle. What stayed in
`AppTemplate.Api` is what this project may not know: the three files importing
`AppTemplate.Infrastructure.*`, the reference page's policy, and the telemetry that names this
host's own database and assembly.

`AppTemplate.Infrastructure.Core` is the infrastructure layer's agnostic half, and it exists because
a module may not reference a sibling: without it, two modules needing one mechanism each keep a
copy. Multilingual mail rendering is the case that pays for it — the identity module and the email
module both send mail, so each would otherwise carry its own renderer. It holds `EmailTemplate`,
which renders a
module's *own* embedded resources and reads the subject out of the template so no caller states it
twice, and `HybridCacheStore`, the one adapter behind `ICacheStore`. `AddCacheStore()` is the only
thing it registers. Both it and `AppTemplate.Api.Core` carry the shape the four projects above do: a
tracked public surface in their own `PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`, `CS1591`
re-enabled, `dotnet pack` over them, and no version.

**Composition is opt-in per feature, and there is deliberately no call that adds all of them.**
`AppTemplate.Application` exposes `AddTodoLists()`, `AddReminders()` and `AddFiles()`;
`AppTemplate.Application.Auth` exposes `AddAuthApplication()`; `AppTemplate.Application.Core`
exposes `AddPurgeExpiredIdempotencyKeys()`. That is what makes a feature removable — deleting its
folder and its one line is the whole operation, and nothing else claims to have registered it —
whereas one entry point scanning a layer's assembly puts every port every feature declares into
every host's graph, which under `ValidateOnBuild` makes each of them mandatory everywhere.
`AddReminders()` is **not** independent of `AddTodoLists()`: scheduling a reminder reaches into the
to-do list's read port, which is the only ownership check made before a reminder is created, so a
host that composes the one without the other resolves nothing.

**A folder under `Features/<F>/` is the plural of the nature word its files carry.** A
`…Repository` is in `Repositories/`, a `…Mapper` in `Mapping/`, a `…Tracker` in `Tracking/`, a
`…Service` in `Services/` — and `Services/` holds nothing that is not one. That is what makes the
tree navigable in both directions: a type name tells you its folder, and a folder tells you what
nature of thing is in it. It is also why the two youngest infrastructure modules have eight and
five words rather than one list shared between them; the *rule* is uniform, the word list is
whatever each module's files need. Several of those folders hold one file, which is the price of
the rule and worth paying.

**The files are held to it too, not only the folder names.**
`FeatureFolderVocabularyTests.EveryFileUnderAFeature_CarriesItsFoldersNatureWord` checks that a file
under `Features/<F>/<Word>/` is named for that word — which is the half that was missing, and how an
`…Access` came to sit in `Services/` and stay: the folder was legal, the type was reasonable, and the
pair was the defect. Three kinds of file are exempt, each named with its reason in that test: a type
in another thing's signature, which does not move away from the thing whose signature it is
(`ContentDecision` beside the policy that returns it, for the reason `Ports/<Port>/` keeps a port's
messages beside the port); a name a framework imposes (`AppUser`, `AppRole`); and the state an
adapter holds (`CachedSigningKeys`). A fourth entry is the rule being dismantled one line at a time,
so it needs its argument in the pull request that adds it.

The vocabulary under `Features/<F>/` is closed, and
`LayoutConventionTests.EveryFeatureFolder_IsNamedFromItsLayersVocabulary` holds it for all eleven
projects above that have a `Features/` directory — including `AppTemplate.Worker`, whose list is deliberately empty: its features
hold their files side by side with no subfolder, so the first subfolder anyone adds has to be
argued for in the pull request that adds it, and written into this file, rather than created
quietly.

Neither of those two rules can catch a project nobody listed, because a project absent from the
dictionary is absent from the loop, and a rule that iterates a list it does not own passes by
saying nothing about what the list omits.
`EveryProjectOnDisk_HasAVocabularyOfItsOwn` reads every project under `Src/` off the disk — not the
infrastructure modules alone, which were merely the ones caught drifting — and requires an entry in
both lists, so **a new project of any layer fails the build until its layout is described here and
there.** Its floor is fourteen projects, so a walk that stopped reading the tree cannot pass by
finding nothing to check.

**An entry may be null, meaning "this project has no such folder today", and that claim is checked
in both directions.** Null is not the same as an empty word list: empty says the folder exists and
its features hold their files side by side, null says the folder is not there at all. So a `Common/`
or `Features/` appearing where the entry says none fails, and so does one vanishing from under a
list of words — which is what keeps the two halves of a split honest. One entry is null for
`Common/`: `AppTemplate.Application.Auth`, which holds one feature and so shares nothing *between*
features. The other two business projects hold `Tagging/` and are checked against that word, and
the failure message a null entry raises tells a reader what to decide rather than what to undo:
something that knows no feature belongs in the layer's `.Core` project, something several features
share as business belongs here. Adding the folder means adding the words it holds to that
dictionary and to the tree above, which is the moment to make that call.

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
- **Every infrastructure module takes `Common/` + `Features/`**, whether or not it serves more than
  one feature. `Email` and `InMemory` have a transverse adapter (`IEmailSender`) and a
  feature-scoped one (`IReminderNotifier`), so the tree says which files leave with the example
  feature. `Identity` serves `Auth` alone and `Storage` serves `Files` alone, and they are filed the
  same way regardless: `Common/` holds what no single subject owns — the user directory every
  service reads, the module-wide options, the two dependency budgets — and `Features/<F>/` holds the
  rest. Local logic loses to uniformity here on purpose, so that reading one infrastructure module
  teaches you how to read the next.
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
because it speaks only in domain types. Every other port belongs to the application layer, because it speaks
in DTOs or platform concerns — and which of the layer's three projects declares it follows how many features
need it. A port one feature reaches for goes in `AppTemplate.Application/Features/<F>/Ports/<Port>/`, or in
`AppTemplate.Application.Auth/Features/Auth/Ports/<Port>/` when the feature is authentication; a
cross-cutting one every feature may reach for — the clock, the caller's identity, the commit boundary, the
mail relay, the leader lease, the idempotency store — goes in `AppTemplate.Application.Core/Common/Ports/`,
or beside its own subject when it has one, as `IIdempotencyStore` does in `Common/Idempotency/`.
`AdapterVisibilityTests` enforces this by recognising a repository contract from
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
| `…Repository`, `…Store`, `…Table`, `…Queries` | the four ways this template reaches storage — see [Decisions already made](#decisions-already-made-and-the-shape-they-impose) for which is which |
| `…Service` | an injected implementation that has dependencies |
| `…Mapper` / `…Mapping` | a mapping, injected / static — and named for what comes **out** of it |
| `…Extensions` | a static class of extension methods, filed with the type it extends — `ResultExtensions` in `Common/Results/`, `CurrentUserExtensions` in `Common/Ports/`. In a host's `Common/` it is the composition class holding that folder's `AddX`/`UseX` |
| `…Options`, `…Validator`, `…Policy`, `…Consumer`, `…Dto`, `…Controller`, `…Request`, `…Response` | as they read |

Two rules follow from having been broken:

- **A word names one notion in this repository.** `Outcome` once named both a use case's return
  record and the enum inside a port's, and `Policies` named both a business rule under
  `Features/<F>/Policies/` and the file holding an `AddApiX`. When a second meaning appears, one of
  the two is wrong — find which and rename it, do not document the ambiguity. Two more that were:
  `Access` named what `Service` already named, and `Verdict` named what `Decision` already named —
  a policy's chosen action, as opposed to the `Status` an observation reports.
- **A port is a port at both scopes, and the word says so.**
  `AppTemplate.Application.Core/Common/Ports/` holds the ones every
  feature reaches for — the clock, the unit of work, the mail relay — and
  `AppTemplate.Application/Features/<F>/Ports/<Port>/`
  the ones one feature does. There is no `Abstractions/` in the application layer: every interface
  it declares is an abstraction, so the word sorted nothing, and it hid two interfaces the layer
  *implements* rather than consumes among the ones infrastructure satisfies. That reasoning is
  `AppTemplate.Application.Core`'s to keep, since `Common/` is its folder alone, and those two live
  with their subject there — `IUseCase` in `Common/UseCases/`, `IDomainEventConsumer` in
  `Common/Events/` — which is what let `PortConventionTests` drop the exclusion list it needed to
  tell them apart. `AppTemplate.Domain.Core` keeps a `Common/Abstractions/`: `IAuditable` and
  `IVersioned` are opt-in contracts a persistence row satisfies, not capabilities the domain calls
  out for.
- **The port carries the nature word, and the adapter is the port without its `I`.**
  `IUserAccountsService` is implemented by `UserAccountsService`; `ISecurityEventLog` by
  `SecurityEventLog`, because `Log` already says what it is. A qualifying prefix is right in two
  cases. The first is a port several modules implement, where the prefix tells them apart:
  `MailKitEmailSender` and `InMemoryEmailSender` both satisfy `IEmailSender`. The second is a
  single adapter whose technology is visible at the call site — `EfUnitOfWork`, because saving is
  the one place the choice of Entity Framework shows, and `PostgresLeaderLease`, because which
  store the lock is taken in is a property a caller has to reason about.
  A port's *folder* keeps the capability name — `Ports/UserAccounts/IUserAccountsService.cs` — and
  that is not cosmetic: a folder named for the interface would put a namespace and a class of the
  same name in scope of each other, which is CS0118 at every consumer.

Banned outright: `Manager`, `Helper`, `Utils`, `Processor`, a bare `Handler`, and `Composer`. Each
of them names "code" rather than a thing, and each attracts whatever nobody could classify. There is
no `Utils/` folder in this repository and there is not meant to be one; needing it means a name is
missing, not a folder.

## Comments

Minimal and short. A comment says **what** something does, or **how** it works, when the signature
does not. It never says why the thing has this shape rather than another. The test is: **if I delete
this comment, can someone introduce a bug?** If not, it goes.

A developer arriving on this repository has to be able to understand the architecture and the code
by navigating them. So:

- **No paraphrase of the code.**
- **No orchestration or construction commentary** — what was weighed, what was rejected, why this
  call sits here. That is noise between the next reader and the code.
- **No narration of the repository's own history.**
- **XML doc only when it says something the signature does not**, and not on every public member by
  reflex.

**Rationale lives in `docs/`.** Measurements, rejected options and arbitrations belong in
`docs/ARCHITECTURE.md` and in `docs/plans/`, where a reader who wants them can address them
directly. A design decision worth keeping is worth a section there, not a paragraph above a method.

<!-- narrative-ok: stating this rule requires quoting the phrases it bans -->
`Tools/CheckNarrativeComments.cs` executes the history half of that rule over every `.cs` and
`.md` file, `CHANGELOG.md` excepted — narrating history is what a changelog is for. Its pattern
list ("this used to…", "the old implementation…", "fixed the bug where…") is deliberately narrow,
because the same words are legitimate or banned depending on the tense they carry: "a v2 added
later would show up inside the v1" is design rationale, and no regular expression separates it from
a sentence about this repository's past. A line that cannot avoid the construction carries a
`narrative-ok: <reason>` marker, and the marker count is printed so exemptions cannot spread
unnoticed.

## Tests

xunit.v3 + Shouldly + NSubstitute. **Not FluentAssertions** — its licence changed and it is not
coming back.

- `TestContext.Current.CancellationToken` in async calls. xUnit1051 is an error here.
- `Arg.Any<CancellationToken>()` for NSubstitute placeholders, never `default`.
- The tree mirrors `Src/` exactly.
- A new test project needs `<IsTestProject>true</IsTestProject>` and to be added to
  `AppTemplate.sln`. The first is what makes `Directory.Build.targets` mark it executable, without
  which xunit.v3 refuses to build it; the second is what makes it run at all, and CI asserts on it.
  It needs no runner package: `xunit.v3` brings the in-process runner, and `dotnet test` drives it
  because `global.json` names Microsoft.Testing.Platform.

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

### What a derived project inherits here

A project generated from this template receives the whole of `Tests/`, fixtures included. That is
the delivery mechanism rather than a gap, and it is why there is no test-kit project to reference:
a kit could only hold the fixtures that name nothing of this product, which is the easy fifth of
them, and the two a derived project actually wants could not go in it.

**Seven fixtures name no product type and can be leaned on as they are** — 356 lines between them.
Under `Tests/Integration/AppTemplate.Api.IntegrationTests/Infrastructure/`: `ApiJson`, the
serialiser settings a raw response is read with; `CapturedLogs`, which makes an assertion about a
log entry possible; `SecurityHeaderAssertions`; and `TestClientAddressStartupFilter`, which is what
lets a test choose the address the rate limiter partitions on. Under the identity project's
`Fixtures/`: `DatabaseReadiness`, `Rendezvous`, which is how two concurrent contexts are made to
race deliberately, and `AnonymousAuditActor`.

**Two name the product and have to be edited, and they are the two that matter.** `ApiFactory`
names ten product namespaces and `IntegrationTestBase` eight: the modules a test host replaces, the
options it overrides, the use cases it reaches past HTTP. Nothing can be done about that — a fixture
that boots this application knows this application — so treat them as your own code from the first
commit, and expect the edit to be a real one when a feature is removed or a module swapped.
`docs/REMOVING-THE-EXAMPLE-FEATURES.md` names what each removal costs in them.

The one duplication actually present in the fixtures is the PostgreSQL container builder: five
lines, three times, differing only in the database name. That is under the bar this file sets for
an extraction, and it stays.

### The load smoke test

`Tests/Load/smoke.js` is a k6 script, and CI runs it **non-blocking**. That is the design, not a
concession: a hosted runner's timings are not a property of this code, so a threshold tight enough
to mean anything on real hardware would be noise there — and a red build nobody can act on is a red
build people learn to ignore. Its thresholds describe a **broken** system (requests failing,
ten-second responses), never a slow one, and a `429` counts as a pass, because the rate limiter
doing its job under load is the correct outcome and a test that punished it would push someone to
loosen the limit to get a green.

It exists because every timeout, pool size and rate limit in this template is chosen by reasoning,
and no other test in the suite observes the result under concurrency. Run it against a stack of
your own:

```bash
docker compose up -d --build
k6 run -e BASE_URL=http://localhost:8080 Tests/Load/smoke.js
```

### Coverage and the architecture tests

NetArchTest resolves each type through `Type.GetType(name, throwOnError: true)`, and that
resolution is sensitive to how the assembly it is looking in was instrumented — it fails outright
against a Coverlet-instrumented one. The collector the platform uses does not break it: all 131
rules in `AppTemplate.Architecture.Tests` pass with the twelve product assemblies that project
references instrumented, which is why coverage is collected over the whole solution in one
invocation.

If you change collector or its settings, re-run that suite under coverage before trusting a green
elsewhere. It is the one project whose failure mode is a thrown resolution rather than a wrong
number.

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
- **An `Options/` folder shadows `Microsoft.Extensions.Options.Options`** for every file under the
  namespace that encloses it — the same enclosing-namespace lookup as above, one word at a time. So
  inside the identity and storage modules and their mirrors, `Options.Create(…)` does not compile:
  use `new OptionsWrapper<T>(…)`, or qualify it in full. Only the bare name is affected;
  `IOptions<T>` and `IValidateOptions<T>` resolve as usual.
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
signatures at each step, if this checklist is not enough on its own;
[docs/REMOVING-THE-EXAMPLE-FEATURES.md](docs/REMOVING-THE-EXAMPLE-FEATURES.md) is the reverse
operation, and the only description of it worth following.

Folders under `Features/` carry the feature name in the plural (`TodoLists`); file and type names
carry the aggregate in the singular (`TodoList`). Every step below is enforced by tests rather than
by the compiler, so run `dotnet test Tests/Architecture` before pushing.

1. **Domain** — `AppTemplate.Domain/Features/<F>/`: the aggregate root in `Entities/`, sealed, value
   objects in `ValueObjects/`, events in `Events/`, and the repository contract in `Repositories/`.
   The root derives from `AppTemplate.Domain.Core`'s `AggregateRoot<TId>` and each event implements
   its `IDomainEvent`; a feature adds nothing to that project, which is the point of it being one.
   Invariants belong in the constructor, the factory, and `Rehydrate` — all three, or a stored row
   can produce an aggregate that breaks its own rules. An event no consumer handles has to be named
   in `DomainEventTests._deliberatelyUnconsumed` with its reason, or that rule fails.
2. **Application** — `AppTemplate.Application/Features/<F>/`: one folder per operation under
   `UseCases/{Commands,Queries}/<Operation>/`, holding the command or query record, its named
   interface, the use case, and its FluentValidation validator together. Any port that is not the
   repository goes in `Ports/<Port>/`, next to the messages that cross it, and declares at most four
   operations. Read models more than one operation shares go in `Dtos/`; the feature's failure
   vocabulary goes in `Errors/`. Validate against the *trimmed* value if the domain normalises.
   Everything the feature is built *from* — `Result`, `Error`, `IUseCase`, `PageRequest`,
   `SortOrder`, `VersionPrecondition`, the cross-cutting ports — comes from
   `AppTemplate.Application.Core`, and a feature adds nothing to that project either.
   Then give the feature its own `AddX()` in `ApplicationModule`, and call it from each host that
   offers the feature: registration is opt-in per feature, so a feature nobody composes is
   registered nowhere. Within that method the use cases and validators of the vertical are
   discovered by namespace; **a domain-event consumer and anything under `Services/` are not** —
   bind each one by hand there, or it compiles and never runs.
3. **Persistence** — `AppTemplate.Infrastructure.Persistence/Features/<F>/`: the `*Record` in `Models/`, its
   `IEntityTypeConfiguration` in `Configurations/`, the mapper in `Mapping/`, the tracker in
   `Tracking/`, the repository implementation in `Repositories/`, read-side projections in `Queries/`.
   Register them in `PersistenceModule` — the read-side port included — and then in `AppDbContext`:
   its schema constant, its `DbSet`, and its `builder.ApplyConfiguration(new …())` call.
   **`PersistenceModule` applies no configuration**; `OnModelCreating` names each one by hand, and a
   configuration nobody names is inert while every gate stays green.
   `PersistenceModelTests.EveryEntityTypeConfiguration_IsAppliedByTheContext` is the guard.
   **A tracker must resolve as one instance under every contract it serves** — three independent
   registrations give three instances, and every write then persists nothing, silently.
   `SharedInstanceRegistrationTests` is the guard.
4. **API** — `AppTemplate.Api/Features/<F>/`: the controller in `Controllers/`, request records in
   `Contracts/Requests/`, response records in `Contracts/Responses/`, and the mapping between them
   and the application's DTOs in `Mapping/`. No endpoint accepts `PATCH`. Endpoints are
   authenticated by default; opting out needs an explicit `[AllowAnonymous]` **and** the action's
   name in `HttpSurfaceTests._anonymousActions`.
5. **Tests** — mirror each of the above, down to `Features/<F>/`.
6. **Migration** — `dotnet run Tools/Tasks.cs migration-add <Name>`, then confirm
   `has-pending-model-changes` reports nothing. An empty migration means step 3's `AppDbContext`
   edits are missing, not that there was nothing to do.

## Migrations

Generate with `dotnet run Tools/Tasks.cs migration-add <Name>`. The application applies migrations
at startup **only in Development**; a deployment applies them as a separate step from a bundle
(`dotnet run Tools/Tasks.cs migration-bundle`). Applying them at startup in production would give the
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

**The refusal of an outbox was re-examined against a feature that could have overturned it, and it
holds.** The `Files` feature looked like the counter-example: a thumbnail that is never generated
because an event went missing is visible, and bytes that are never reclaimed accumulate. It is not
one. The reclamation is a periodic sweep that re-derives its own precondition — an object no row
references is garbage — so the deletion event is a fast path that shortens an interval and nothing
more, in exactly the shape `CancelRemindersOnTodoItemCompletedConsumer` already had. Derivative
generation, when a project adds it, gets the same treatment: sweep for available files without a
derivative, and let the event only make it prompt.
The thing that nearly cost this its meaning was not the design. Three files in that feature
described the deletion consumer's behaviour in detail before the consumer existed, and every test
was green, because nothing related the events raised to the consumers registered.
`DomainEventTests` now does: every event is either consumed or written into a list saying why it is
not.

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

**There is one outbound HTTP budget, it is written once, and it is a default rather than a call.**
`AppTemplate.Presentation.Core`'s `AddOutboundHttp()` installs it on `IHttpClientFactory`'s
defaults, so a module that registers a typed client inherits 10 s per attempt, 30 s in total, three
retries with jitter, a circuit breaker and a concurrency bound without knowing any of it exists. A
default beats a shared method that each module has to remember to call — nothing can forget a
default. And one policy is what makes it a policy at all: the modules that call outwards, mail and
identity, are composed by more than one host, and a budget enforced in one host only is worse than
none, because the host that misses it is the one nobody watches. The alternative shape stays refused
for its own reason — putting `HttpClient` behind an application port is the abstraction
`docs/ARCHITECTURE.md` rejects by name. Two rules guard the two escapes:
`NoType_ConstructsItsOwnHttpClient` and `EveryHost_InstallsTheOutboundPolicy`.
**Retry is an allow-list of the safe verbs** — GET, HEAD, OPTIONS, TRACE — and not the package's
own deny-list, which would retry any verb it does not name. PUT and DELETE are out despite being
idempotent by specification, because that promise belongs to the server at the other end and a
default applies to servers nobody here controls. Relax it for one client whose server you know;
never widen the default. **The 30 s total is bound to `RequestTimeouts:Default`'s 5 minutes**: an
outbound call happens inside an inbound request, so the enclosing budget has to be the longer of
the two, for the same reason `RequestTimeoutsOptions` gives about the layer below it. If either
number moves, re-read both.

**Exclusivity between hosts belongs to the operation, not to the loop that starts it.**
`ILeaderLease` is an application port, and `FireDueRemindersUseCase` is what takes it — not
`ReminderBackgroundService`. A `BackgroundService` is a trigger; a guard placed there protects the
timer's callers and nobody else, and the two purges are already exposed over HTTP by
`MaintenanceController` as the standing reminder that a second caller does turn up. The adapter is
a PostgreSQL session-level advisory lock (`Common/Leases/`), chosen because losing the process
releases the lock rather than stranding a lease until a timer says otherwise. **It is not a fencing
token** — leadership can be lost mid-run — so anything run under it still has to survive a second
host starting it. `PortConventionTests.EveryApplicationPort_HasAConsumerInTheApplicationLayer` is
what holds the first sentence: a port this layer declares and never calls is a decision that has
left the layer, and the fix is to move the decision back, not to move the file somewhere the rule
does not look.

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

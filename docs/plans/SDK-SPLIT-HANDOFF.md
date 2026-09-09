# Handoff — where the SDK split stands, and what is left

**Written:** 2026-09-07, at the end of wave 4. **Amended 2026-09-08**, when wave 6's five open
questions were settled and the separation of authentication was scheduled, and again at the end of
wave 7.
**Read this before touching anything.**

This directory is exempt from `Tools/CheckDocPaths.cs`, so paths below may name a tree that does not
exist yet.

Read these six first. They are the plan of record and the analysis behind them must not be
redone:

| Document | Answers |
|---|---|
| `docs/plans/SDK-SPLIT-PLAN.md` | The seven waves, the order, the preconditions, the exit gate |
| `docs/plans/SDK-SPLIT-TARGET.md` | What each project is: path, folders, namespaces, references, packaging |
| `docs/plans/SDK-SPLIT-DECISIONS.md` | Why, including every rejected option and its reason — **45 entries now, 42 numbered** |
| `docs/plans/SDK-SPLIT-BLAST-RADIUS.md` | What else changes: rules, tests, tooling, docs, and the known gaps |
| `docs/plans/AUTH-SEPARATION.md` | The separation of authentication: measured state, decisions A1 to A8, waves A to D. Scheduled after wave 7, self-contained |
| `docs/plans/DERIVED-PROJECT-ERGONOMICS.md` | What a derived project writes itself: the duplication re-measured, decisions D1 to D6, wave E scheduled, waves F and G offered. Taken after the separation of authentication, self-contained |

`SDK-SPLIT-DECISIONS.md` has grown thirteen entries and two corrections since it was written.
Entries **15 to 32** and the two sections at the end (`Two corrections to …TARGET.md`, and the
correction inside decision 7) were added while implementing, and they override the originals where
they differ. **19 to 27 were settled at the start of wave 5, and 28 to 30 came out of implementing it,** and amend `…TARGET.md` on six points:
the packages of `Api.Core`, its composition list, its public surface, its promotion count, the
API-reference policy, and what stays in the API host.

## How work leaves this repository

Waves 0 to 6 and the audit pass between them are committed on `main`. **Never run `git commit` on
your own initiative** — the owner asks for it, splits the commits by subject, and they carry no
co-author trailer. Leave your own work staged (`git add -A` at the end of a wave) until then.

## What is done, and verified

| Wave | Subject | State |
|---|---|---|
| 0 | Prepare the fitness harness | done |
| 1 | `AppTemplate.Domain.Core` | done |
| 2 | `AppTemplate.Application.Core` | done |
| 3 | `AppTemplate.Application.Auth` + per-feature registration | done |
| 4 | `AppTemplate.Presentation.Core` | done |
| 5 | `AppTemplate.Api.Core` | done |
| — | The audit pass, all three strands | done |
| 6 | The missing pieces | done |
| 7 | Documentation and close | done; the whole gate runs, and the step that used to be the exception is explained below |
| — | `docs/plans/AUTH-SEPARATION.md`, waves A to D | done; A7 closed on 2026-09-09 — the controllers stay in `AppTemplate.Api`, and that entry carries the bench that measured why the project was free to build and bought nothing |
| — | The comment-convention cleanup pass | done, 2026-09-09; all fifteen projects under `Src/`, and the standard it settled on is below |

Measured at the end of wave 7, not remembered:

- **15 projects** under `Src/`, **18** under `Tests/` — 15 unit, 1 architecture, 2 integration.
- **3328 tests**, 0 failing, 0 skipped (`dotnet test --solution AppTemplate.sln`).
- **Line coverage 94.87%** in Debug (7329/7725) and **96.09%** in Release (6099/6347), both with
  Docker present so both integration suites contribute. The floor is **90**, up from 88, by the
  same arithmetic the previous move used: the margin is held constant and the number follows.
- **131 architecture rules**, all passing — two more than wave 4 left, both added because the split
  created the gap they close: a host must install the core pipeline before any middleware of its
  own, and an SDK project without the ASP.NET framework reference may not acquire it from a package.
- **Six** projects declare `IsPackable`: `Domain.Core`, `Application.Core`, `Application.Auth`,
  `Infrastructure.Core`, `Presentation.Core`, `Api.Core`. All six `dotnet pack` cleanly.
- Both container images build.
- `dotnet run Tools/Tasks.cs verify` and `dotnet format --verify-no-changes` both clean.

### The five SDK projects as they stand

Each carries: `IsPackable`, `PackageId`, `Description`, `Authors`, **no version**;
`Microsoft.CodeAnalysis.PublicApiAnalyzers` with `PublicAPI.Shipped.txt` and
`PublicAPI.Unshipped.txt`; `CS1591` re-enabled via `<NoWarn>$(NoWarn.Replace('CS1591', ''))</NoWarn>`
so every public member is documented; and an `InternalsVisibleTo` naming exactly its own mirror.

Public API baselines were generated mechanically from the analyser's own `RS0016` output.
**Regenerate the same way** rather than hand-editing: build the project, collect the symbol from
each `error RS0016`, sort, write with a `#nullable enable` first line. The counts are deliberately
not written here — the two `PublicAPI` files of each project are the only place they stay true, and
a number in this sentence had already drifted twice by the time anyone read it. `RS0017` is the
other direction and matters as much: it names a symbol the baseline holds and the code no longer
has, so a removal is as explicit a diff as an addition.

`Api.Core`'s 134 is the number decision 26 produced: everything internal first, then exactly the
promotions the compiler demanded. Five types were promoted and each has a reason a reader can
check — an options property's enum, the rate-limit policy name a controller carries, the error
mapping a controller calls, the window a test host replaces, and one new named delegate.

## The exit gate, at every wave

```
dotnet build AppTemplate.sln
dotnet run Tools/Tasks.cs verify
dotnet run Tools/Tasks.cs format-fix
dotnet test --solution AppTemplate.sln
dotnet pack <each SDK project>
docker build -f Src/Presentation/AppTemplate.Api/Dockerfile .
docker build -f Src/Presentation/AppTemplate.Worker/Dockerfile .
```

Two things to know about it:

- **`dotnet restore` after every `.csproj` reference change.** Building `--no-restore` against a
  stale `project.assets.json` produces `CS0234` on a namespace that is genuinely referenced, and the
  error blames the consumer rather than the missing restore. This cost real time in wave 3.
- **One known intermittent failure, now addressed.**
  `AppTemplate.Api.IntegrationTests.Storage.SignedGrantTests.AGrantWhoseLifetimeHasElapsed_IsRefused`
  failed 2 of 4 full-suite runs at the end of wave 4, and passes 3/3 in isolation and 341/341 with
  its own project. It is a grant-lifetime assertion under parallel load; neither the test nor its
  subject is modified. It is **not** caused by the split. See the open questions.
- **A second intermittent failure, found and fixed on 2026-09-09.**
  `AppTemplate.Worker.UnitTests.Features.Reminders.ReminderBackgroundServiceTests.Stopping_IsLogged_EvenWhenTheStopLandsMidIteration`
  failed once inside `dotnet run Tools/Tasks.cs verify`, then passed 3/3 on its own and 3336/3336 in
  a full `dotnet test --solution`. The shape was in the test: a 50 ms `Task.Delay` was what put the
  hanging use case mid-flight before the stop was asked for, and under full-solution parallel load
  50 ms is not a guarantee that the iteration has started at all — so the stop could land on a loop
  that had not begun, which is not the case the test names.

  **The fix is a signal instead of a duration**, which is the shape decision 31 established: each of
  the three hanging fakes exposes `HasEntered`, set as the pass starts, and each test waits on it
  through `BackgroundServiceProbe.WaitUntilAsync` — the helper this suite already had, with its 30 s
  ceiling and a failure message naming what did not happen. Five call sites across the three worker
  loop suites, and the 50 ms sleep is gone from all of them.

  **What was deliberately left alone:** `Loop_NeverCallsTheUseCase_WhenFiringIsDisabled` still sleeps
  five intervals, because a negative assertion has no signal to wait for and its flakiness is
  one-sided — load gives the loop *fewer* chances to call, never more, so a slow machine cannot make
  `CallCount == 0` wrong.

  **Proved it can still fail**, rather than assumed: changing the product's stop log line to
  something else fails the test in 78 ms. Then `verify` twice end to end, 3336 tests, none failing.

## What each wave produced, and the one thing offered but never scheduled

### Wave 5 and the audit pass — done, and what they left behind

`Api.Core` holds 45 files in eleven `Common/` folders with `ApiCoreModule.cs` alone at its root;
`AppTemplate.Api`'s own `Common/` holds three folders, and the criterion for what stayed is
executable rather than stylistic — exactly the three files importing
`AppTemplate.Infrastructure.*`, plus the API-reference policy and the telemetry residue that names
this host's own database and assembly. `Program.cs` is 138 lines.

The audit pass closed all three strands: the `.Core` `Common/` shape is kept (decision 19), the two
agnosticism questions were answered by the owner, and the business `Common/` halves are occupied by
`Common/Tagging/` in both layers (decision 32) — reached after a measured search that falsified
decision 27 and retracted it.

**Read decisions 19 to 32 before touching either presentation project or either business `Common/`.**
Four of them exist because a test caught something a reading had missed, and each says which test.

### Wave 6 — the missing pieces. Every question it was blocked on is answered.

Three subjects, not four, and all three are implemented and green. Decisions 33 to 37 settle each one and say what they rejected; decision 35 carries a correction and its resolution, because the consumer it first named turned out not to be a cache at all.

| Subject | Shape | Decision |
|---|---|---|
| `PeriodicJob` | A **loop primitive**, not a `BackgroundService` base class: one interval, one asynchronous iteration. Each service composes one instance, or three. `Common/Jobs/` in `Presentation.Core`, which is why that project has four `Common/` folders today and not five | 33 |
| The three worker loops | The **structure** of each survives — where the enabled test sits, log level and vocabulary, span and tag names, counter shape, timer topology, scope granularity, how the stop propagates. The two **defects** are fixed: the maintenance loop gains a counted and warning-logged disabled purge with its consequence, and maintenance and reminder gain the shutdown log the file loop guarantees | 34 |
| Email templating | A new **`AppTemplate.Infrastructure.Core`**, holding the engine taken from its two existing copies. Not `Application.Core`: reading HTML out of an assembly is not that layer's decision, and the duplicated file says so itself | 37 |
| Cache | `ICacheStore` in `Application.Core/Common/Ports/`, one `HybridCache` adapter in `Infrastructure.Core`, and the consumer is a **new** read in both features that own tags — the tags an owner has already used, for a picker. No existing read tolerated staleness, and the correction inside decision 35 measures all four. **No output caching** | 35 |
| ~~A reusable test kit~~ | **Withdrawn.** Measured: 356 lines of the fixtures name no product type, and the two a derived project wants name ten and eight product namespaces and cannot move. A derived project receives all of `Tests/` by generation, so copying is the delivery mechanism | 36 |

One rule is amended by wave 6: `InfrastructureModules_ReferenceOnlyPersistenceHorizontally` becomes
"only Persistence or the infrastructure `.Core`", keeping its non-vacuity assertion. That is what
makes a shared infrastructure foundation legal without opening sideways references generally.

### Wave 7 — documentation and close. Done; nine lots, and what each produced.

The lots and their order are in `docs/plans/SDK-SPLIT-PLAN.md`; decisions 39 to 42 settled what was
open. Lot 1 was committed ahead of the rest, which is why the arbitrations of this wave exist in
these documents rather than only in a conversation.

Three findings worth keeping, because none of them was predicted by the plan:

- **`Api.Core` was missing from `docs/ARCHITECTURE.md`'s layer table and Mermaid diagram**, not only
  `Infrastructure.Core`. The claim above that "the other five `.Core` projects are already described
  everywhere" was false of that document, and the path gate cannot see it: it checks that a cited
  path exists, never that an existing project is cited. Both projects are in the table and the
  diagram now, and the arrow count in the prose beneath it was re-counted twice as a result.
- **Four other statements were stale rather than merely incomplete**, each found by reading the code
  the sentence described: both hosts compose **ten** lines and not nine (`AddCacheStore` is the
  sixth); `CONTRIBUTING.md` claimed three business `Common/` entries were null when tagging occupies
  two of them, and put the project-walk floor at thirteen where the rule says fourteen; the
  canonical tree carried the pre-split `AppTemplate.Api` folder list and had no `Api.Core` or
  `Infrastructure.Core` row; and `README.md` said `AppTemplate.Application` has "no `Common/`
  today". A count in prose is worth re-reading off the thing it counts, every time.
- **`--configuration` does not reach `Tools/Tasks.cs` without a `--` separator**, because
  `dotnet run` claims that option for itself. A "Release" coverage run without it executes the
  `bin/Debug` DLLs and reports the Debug figure under the Release heading — it looked plausible
  because it *was* a real measurement, of the wrong thing. `coverage.minimum` now states the
  invocation. The file's own doc comment had said so all along.

**The exit gate runs end to end, including the step that used to be the exception.**
`dotnet run Tools/Tasks.cs verify` ends on `dotnet ef migrations has-pending-model-changes`, and
`dotnet tool restore` used to fail there with
`Settings file 'DotnetToolSettings.xml' was not found in the package` — on a nupkg that downloads
intact and carries `tools/net8.0/any/DotnetToolSettings.xml`, with the SDK extracting nothing to the
package store.

**It is the SDK, and only one of the two installed.** Measured: 10.0.111 restores tools fine, and
10.0.300 — the one `global.json` pins — fails for *every* tool, `dotnetsay` as much as `dotnet-ef`.
Not the package version (10.0.8, 10.0.9 and 9.0.11 fail identically), not `rollForward`, not the
sandbox.

**The fix is machine-local and changes nothing in this repository.** Restore the tool once from a
scratch directory whose own `global.json` pins the working SDK; that populates
`~/.nuget/packages/dotnet-ef/`, and the repository's own restore then finds it already extracted and
succeeds. Deleting that cache entry reproduces the failure, so this is worth knowing rather than
forgetting. **Rejected — pinning `global.json` to 10.0.111:** that file decides which SDK every
developer and CI uses, and downgrading all of them to work around one machine's tool-extraction bug
is a wide change for a local problem. **Rejected — installing the tool globally:** a local manifest
takes precedence, so it would not be the one that runs.

### Two chantiers of their own, both done

**The separation of authentication** — `docs/plans/AUTH-SEPARATION.md`. **Done**, and its four forks
are closed — A6 and A7 on 2026-09-09.
Read that document; it is self-contained and every measurement in it was taken on 2026-09-08, or on
2026-09-09 where an entry says so, so nothing needs re-measuring.
The short version: the business domain already knows only an opaque `Guid OwnerId` with **no**
foreign key to the identity tables, `ICurrentUser` is already scheme-agnostic, and **no code path
commits an identity write and a business write together** — so the real work is that the two halves
share one `DbContext`, one migrations history and one unit of work. Four waves: name the subject,
move the agnostic EF mechanisms into `Infrastructure.Core`, give authentication its own storage,
document and close. **There is no `Domain.Auth`** and decision A1 says why.

**The comment-convention cleanup pass.** `CONTRIBUTING.md`'s `## Comments` section forbids
orchestration and construction commentary and sends rationale to `docs/`. Every file written or
touched from 2026-09-08 follows it; the existing tree is being brought into line one project at a
time, because a single diff over the whole tree is unreadable and every deletion is a judgement.

**All fifteen projects under `Src/` are done** (2026-09-09). Line comments across the tree: **1635
to 934**, and the tree is **420 lines shorter**. `verify` green at 3336 tests after each project.
Per project, the four that carried the most: `Infrastructure.Persistence` 330 to 164,
`Infrastructure.Auth` 219 to 152, `Api.Core` 176 to 127, `Application` 170 to 132. `Api`'s
`Program.cs` went from 124 comment lines to 33 and the worker's from 40 to 20. **Eleven blocks of
five or more consecutive `//` lines remain in the tree**, each one a fact the code cannot state.

**Three rules came out of doing it, and they are what the next project should be read against.**

- **Rationale is moved, not deleted.** What a remark argued and `docs/` did not already carry went
  into `docs/`: the HTTP-boundary section of `ARCHITECTURE.md` gained the wire-contract rule with the
  three projections that earn it and the reason a provider name is a field rather than a route
  segment, and `ADDING-A-FEATURE.md` gained the three controller conventions that four controllers
  were each stating in their own words. Deleting those would have lost something a reader needs.
- **A hazard is not commentary.** The test `CONTRIBUTING.md` states — can someone introduce a bug if
  this goes — keeps a comment that a rule of thumb would cut: that `[AllowAnonymous]` on
  `AuthController`'s class would defeat the `[Authorize]` on two actions and serve a caller's profile
  to anyone, that the XML generator collects the comments of the assembly making the `AddOpenApi`
  call, that the 64 KiB body cap is why depositing a file is two requests.
- **In the API host, a `///` on a contract or an action is product output**, not internal
  commentary: it becomes the OpenAPI document's `description` and `summary`, and
  `OpenApiDocumentTests` asserts one of them. Those are trimmed of repository-internal argument and
  otherwise left alone — the reader they serve is a client, not a maintainer.

**Found while doing it, and both were stale rather than merely verbose.** `MaintenanceController`'s
remark said the endpoint exists "rather than an in-process scheduled timer" — the worker's
maintenance loop runs exactly those two use cases on exactly such a timer, so the sentence described
a tree that no longer exists. And `docs/ARCHITECTURE.md` claimed two couplings hold authentication
in place, naming `IIdentitySeeder` as one; wave C moved the seeder into the auth module, and the test
had been renamed to say *one* coupling while the prose was not.

**What the standard turned out to be, stated for whoever reads a diff of this pass.** A comment
survives if deleting it lets someone introduce a bug. It is shortened if it states that fact in more
words than the fact needs. It is deleted outright if it paraphrases the line under it, argues for the
shape the code already has, points at another file for the reasoning, or narrates this repository's
own past. And where a comment existed because the code was unclear, the **code** changed: the tracker
registration became `AddScopedTracker<TTracker, TTrackerPort>`, and both composition guards became
`AlreadyComposed(services)`.

**Four statements were stale rather than merely verbose**, each found by reading the code the
sentence described: `MaintenanceController` claimed to exist "rather than an in-process scheduled
timer" while the worker runs those two use cases on exactly such a timer; `AuthModule.cs` said
`IdentitySeedOptions` is bound by the persistence module, thirty lines below binding it itself;
`docs/ARCHITECTURE.md` named two couplings holding authentication in place where the test it cites
names one; and `AuthModule` explained a lockout setting by describing the defect that predated it.

**The `///` half is barely touched**, by design: 10 589 lines across the tree, and in the API host
they are the OpenAPI document's own `description` and `summary`, which `OpenApiDocumentTests` asserts
on. In the six packable projects `CS1591` is re-enabled, so a public one can be shortened and never
removed. What was cut there is repository-internal argument inside a client-facing sentence.

### Roughly 126 lines of measured, agnostic duplication — now scheduled, and re-measured larger

Found by the search that preceded decision 32, and measured with `diff` rather than judged. All of
it is agnostic, so all of it belongs in a `.Core` project and none of it in a business `Common/` —
which is why it is listed apart from wave 6 rather than inside it.

| Destination | What | Net |
|---|---|---|
| `Domain.Core/Common/Primitives/` | an `AuditableAggregateRoot<TId>` holding the `IAuditable`/`IVersioned` block that is **byte-identical, comments included, in all three aggregates** | 46 |
| `Application.Core` | the paginated read-by-owner use case — `diff` between the two is 100% type substitution | ~28 |
| `Application.Core` | a `FeaturePageRequest<TFilter>`; `diff` is pure substitution | 28 |
| `Application.Core` | the request-binder skeleton, whose own doc comment already concedes the copy | ~24 |
| `Application.Core` | `SearchTerm.CreateOptional` — 13 strictly identical lines, twice | ~22 |

It is pure DRY with no product decision in it, and it touches two frozen public surfaces and their
`PublicAPI` baselines, which is the whole of its cost.

**Scheduled on 2026-09-09**, in `docs/plans/DERIVED-PROJECT-ERGONOMICS.md`, which re-measured every
row against the tree as it then stood and found the total larger rather than smaller: the ownership
guard decision 27 withdrew, and the optional-search-term block, belong on this table and were not on
it. Read that document rather than this table — it carries the shape each extraction takes, what
each one rejected, and the two rows this one is missing.

## Decisions taken while implementing, which amend the plan

Read them in `docs/plans/SDK-SPLIT-DECISIONS.md`; summarised here so nothing is missed.

- **15** — an SDK project is recognised by `IsPackable`, not by a list of names.
  `ProjectReferenceGraph` reads it and exposes `SdkProjects` / `IsSdkProject`.
- **16** — `Application.Core` has **no** umbrella `AddApplicationCore()`. It would register nothing,
  and a call that does nothing reads like the seam that makes the project work.
- **17** — there are **two** `Common/` folders per layer: `<Layer>.Core/Common/` is agnostic of the
  business, `<Layer>/Common/` is the business-shared half. The sorting test is one question.
- **18** — the shared telemetry registration takes the host's `Assembly` (so two hosts do not report
  under one `service.name`), the host's diagnostics arrive through two builder callbacks (a shared
  project naming them would need a reference to the host that references it), and
  `AddLocalizationOptions()` binds and validates and deliberately does nothing else.
- **19** — the `.Core` projects keep `Common/`. The audit's first strand, closed; no rename.
- **20** — API versioning moves into `Api.Core`, which declares both `Asp.Versioning` packages; the
  host keeps `Asp.Versioning.Mvc` because its six controllers each name the `ApiVersion` attribute.
- **21** — the bearer scheme's description names no route.
- **22** — `Api.Core` maps the health endpoints, through `MapCoreHealthEndpoints()` called beside
  `MapControllers()` rather than from inside `UseCorePipeline()`. The `/health` prefix becomes one
  public constant where three literals stand today.
- **23** — the API's observability splits. The rate-limiting meter is what decides it: the limiter
  moves, so its rejection counter must move with it. The host keeps a residue for `AddNpgsql()`, and
  `ObservabilityRegistrationTests` gains the assertion that makes dissolving that residue fail
  honestly instead of blaming a derived project's first instrument.
- **24** — Scalar leaves `Api.Core` entirely, package included. The page's policy is an observation
  about a versioned third-party bundle and belongs beside the reference that pins it. The
  environment check leaves the SDK with it.
- **25** — a host's `Common/` is a third kind beside decision 17's two, and its criterion is
  executable: exactly three of the forty-eight files import `AppTemplate.Infrastructure.*`, and they
  are exactly the three that stay.
- **26** — public is exactly what a host names, and the compiler settles it type by type. The nine
  registration extensions go internal, because a public `Add` with no matching `Use` is a seam that
  cannot be used, and `UseCorePipeline()` is monolithic on purpose.
- **27** — **withdrawn.** It claimed the triplicated owned-aggregate guard belonged in the business
  `Common/`; measured against `CONTRIBUTING.md`'s own rule, the extraction is defensible but lands
  in `Application.Core`, so it does not populate the business half at all. The entry records what the
  measurement established, and turns the matter into a question for the owner.
- **Correction inside decision 7** — the decision promised a test its own limits section forbids.
  "The container builds without Auth" is **false** of this template: the persistence module
  registers `IIdentitySeeder`, which needs a `UserManager` only the identity module supplies. The
  claim is split in two and both halves are asserted:
  `LayerDependencyTests.TheApplicationLayer_KnowsNothingOfAuthentication` and
  `ContainerCompositionTests.RemovingAuthentication_IsHeldUpByOneInfrastructureCoupling_NotByTheApplicationLayer`.
- **Two corrections to TARGET** — each host lost **four** package references to `Presentation.Core`,
  not five; and `Common/Jobs`/`PeriodicJob` are wave 6, so TARGET describes the finished state there.

## Hazards learned the hard way — do not rediscover these

- **Anchor every namespace substitution on the full prefix**, e.g. `AppTemplate.Application.`. A
  shorter anchor silently corrupts `AppTemplate.Infrastructure.Persistence.Common.Idempotency` and
  `AppTemplate.Infrastructure.InMemory.Features.Auth`.
- **And sweep the abbreviated form too.** The strict anchor correctly misses *partially qualified*
  references such as `<see cref="Application.Common.Idempotency.IIdempotencyStore"/>`, which resolve
  relative to the enclosing namespace. Three of those broke the build in wave 2. Grep for the short
  form after every sweep.
- **A rename can reorder `using` directives.** `AppTemplate.Application.Auth` sorts before
  `AppTemplate.Application.Core`, so 80 files went out of order in wave 3. Nothing gates it. Sort on
  the namespace text, **not** including the trailing `;` — otherwise `.Core.Common` sorts before
  `.Core`, which is not what the IDE does.
- **`dotnet sln add` is not idempotent and not predictable.** It may invent a solution folder, may
  add nesting entries, may add none, and **re-generates a different project guid** if the project is
  removed and re-added. This repository already has three separate solution folders all named
  `Presentation`. Always read the file after, verify nesting by hand, and put the guid into
  `.template.config/template.json` **after** confirming it — `Rules/TemplatePackagingTests.cs` is
  what guards that list.
- **A vocabulary rule needs both directions.** `LayoutConventionTests` originally checked only
  folder→vocabulary, so a word whose folder had gone stayed in the list undetected. Both rules now
  check the reverse as well. If you add a rule over a hand-maintained list, ask which direction it
  cannot see.
- **`.editorconfig` sections follow their files.** Three scoped sections exist, for `Error.cs`,
  `Result.cs` and `DomainException.cs`. A section whose path no longer exists produces **no**
  diagnostic — the suppression simply stops applying and the rule becomes an error at the new path.
- **New `.cs` files need a UTF-8 BOM.** `charset = utf-8-bom` for `[*.cs]`, and
  `EnforceCodeStyleInBuild` plus `TreatWarningsAsErrors` turn a missing one into a build error whose
  message does not say so. `.csproj` files carry **no** BOM (`[*]` is `utf-8`), even though ten
  existing ones do.
- **Do not touch `dotnet_diagnostic.CA1515.severity = none`.** It is what makes every deliberate
  `public` type legal; without it the split cannot compile.

- **A project moving off `Microsoft.NET.Sdk.Web` loses nine implicit `using` directives.**
  `Api.Core` is `Microsoft.NET.Sdk` plus a `FrameworkReference`, so the web SDK's implicit set
  — `Microsoft.AspNetCore.{Builder,Hosting,Http,Routing}`,
  `Microsoft.Extensions.{Configuration,DependencyInjection,Hosting,Logging}` and
  `System.Net.Http.Json` — is gone. Nearly every file moved out of `Api/Common/` leans on it:
  `SecurityHeadersExtensions.cs` names `HttpContext`, `IServiceCollection`, `IConfiguration` and
  `WebApplication` without a single `using` for any of them. The compiler reports each one, so
  this is mechanical rather than subtle — but it is 45 files, and each added directive has to
  land in sort order.

## Non-negotiable conventions

- **Namespace = project name + folder path.** No exception, checked over ~600 files.
- Every project has the shape `Common/<Responsibility>/` plus `Features/<Feature>/<Responsibility>/`,
  for all fourteen — the audit kept it (decision 19). A `.Core` project has no `Features/` and may
  not grow one; a business project's `Common/` is earned by naming a business type, not by holding
  something repeated (decisions 27 and 32).
- **One public type per file**, named exactly like the file. **One `.cs` at a project root**, and it
  is the composition file: `Program.cs`, or the name `LayoutConventionTests.ModuleFileName` /
  `QualifiedModuleFileName` accepts.
- File-scoped namespaces only. LF endings. `using AppTemplate.*` sorted on the namespace text.
- **Comments: minimum and short.** A comment says what something does, or how it works, when the
  signature does not — never why it has this shape rather than another. No orchestration or
  construction commentary, no rejected alternatives, no paraphrase, and nothing about the
  repository's own history: no "was", "used to", "previously", "moved from", "now that", and no
  mention of this split. `Tools/CheckNarrativeComments.cs` fails the build on the history half.
  **Rationale belongs in `docs/`** — these plan documents and `docs/ARCHITECTURE.md`. See
  `CONTRIBUTING.md`, section `## Comments`, which is the statement of record.
- `Tests/` is a 1:1 mirror of `Src/`. Each `InternalsVisibleTo` names exactly one assembly — its own
  mirror.
- **Never `git commit`.** Leave work staged. The owner splits the commits by hand, and they carry
  no co-author trailer.
- At most **three agents in parallel**, on disjoint scopes. Git worktrees are unusable: nothing is
  committed, so an isolated checkout would be empty.

## Open questions — ask, do not assume

The repository owner has asked explicitly for no invention and no silent assumption. **All seven are
now closed**; 5, 6 and 7 were answered on 2026-09-08 and each says which wave-7 lot carries it.
Everything wave 6 was blocked on was
settled on 2026-09-08 and is recorded as decisions 33 to 38, and the four forks the separation of
authentication left are closed at the end of its own document. Ask; do not pick.

1. ~~**The `.Core` `Common/` shape**~~ — **closed.** Shape A is kept, for all five `.Core` projects,
   and the reason is not the one decision 4 gave: see decision 19. No rename happens, and `Api.Core`
   is built in its final shape in this wave.
2. ~~**`Features/Maintenance/` in `Application.Core`**~~ — **answered: it stays.** It purges the
   idempotency store, whose port lives in the same project, and no example feature depends on it, so
   "Maintenance" is mechanism maintenance.
3. ~~**`IEmailSender` in `Application.Core/Common/Ports/`**~~ — **answered: it stays**, as an
   agnostic port a derived project may extend in its own business. The owner's framing is worth
   keeping: the SDK exists to hand out reusable building blocks. **Watch it in wave 6:** that wave
   adds an email-templating surface, and a template is named after what it announces — so after a
   feature. A rendering engine may live in `Application.Core`; named templates may not.
4. ~~**`SignedGrantTests.AGrantWhoseLifetimeHasElapsed_IsRefused`**~~ — **closed.** The test proves
   the grant was valid with a control grant of its own instead of with a time window, which removes
   the reading under which a transient `Forbidden` from a warming store counted as "never valid".
   See decision 31, including what that decision does and does not claim.
5. ~~**Two pre-existing defects the docs carry**~~ — **answered: both are fixed in wave 7**, in
   lot 4. `docs/REMOVING-THE-EXAMPLE-FEATURES.md` says "the ten unit and architecture test projects"
   and cites measurements taken against a different tree ("2618 passing", "three rounds of
   `dotnet build`"); the count to write is **15 unit plus 1 architecture**, and the measurements are
   re-taken or dropped rather than restated. `docs/CONFIGURATION.md`'s `Localization` section
   advises setting `CultureInfo.CurrentUICulture`, which `CurrentLanguage`'s own remarks explain is
   impossible under `InvariantGlobalization=true`; that section is corrected to name
   `CurrentLanguage.Tag`, which is what `UseRequestLanguage` actually sets, in both places the prose
   names the culture.
6. ~~**`docs/ARCHITECTURE.md` contains one sentence beginning "There was previously a…"**~~ —
   **answered: rewrite it**, in the same lot, to state what resolves the connection string without
   the comparison. It is rewritten now rather than left to decision A4 of
   `docs/plans/AUTH-SEPARATION.md`, which rewrites the whole section later: a rule the repository
   states about itself is not left broken for the length of another chantier.
7. ~~**Markdown BOM is inconsistent**~~ — **answered: normalise it and write the rule down**, lot 8,
   decision 42. Measured on 2026-09-08: nine files carry a BOM (`CHANGELOG.md`, `CONTRIBUTING.md`,
   `README.md`, `SECURITY.md` and the five under `docs/` other than `DEPLOYMENT.md`) and eight do
   not (`docs/DEPLOYMENT.md`, `deploy/kubernetes/README.md`, and the six under `docs/plans/`).

Anything not on this list and not settled by the four plan documents is also a question. Ask it.

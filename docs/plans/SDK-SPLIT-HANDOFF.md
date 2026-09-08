# Handoff — where the SDK split stands, and what is left

**Written:** 2026-09-07, at the end of wave 4. **Read this before touching anything.**

This directory is exempt from `Tools/CheckDocPaths.cs`, so paths below may name a tree that does not
exist yet.

Read these four first. They are the plan of record and the analysis behind it must not be redone:

| Document | Answers |
|---|---|
| `docs/plans/SDK-SPLIT-PLAN.md` | The seven waves, the order, the preconditions, the exit gate |
| `docs/plans/SDK-SPLIT-TARGET.md` | What each project is: path, folders, namespaces, references, packaging |
| `docs/plans/SDK-SPLIT-DECISIONS.md` | Why, including every rejected option and its reason — **35 entries now, 32 numbered** |
| `docs/plans/SDK-SPLIT-BLAST-RADIUS.md` | What else changes: rules, tests, tooling, docs, and the SDK's known gaps |

`SDK-SPLIT-DECISIONS.md` has grown thirteen entries and two corrections since it was written.
Entries **15 to 32** and the two sections at the end (`Two corrections to …TARGET.md`, and the
correction inside decision 7) were added while implementing, and they override the originals where
they differ. **19 to 27 were settled at the start of wave 5, and 28 to 30 came out of implementing it,** and amend `…TARGET.md` on six points:
the packages of `Api.Core`, its composition list, its public surface, its promotion count, the
API-reference policy, and what stays in the API host.

## Nothing is committed

Everything from waves 0 to 5, and the audit pass after it, is **staged and uncommitted**, on
`main`, over commit `4bbb18a`. About 883 staged paths. The working tree is clean. **Never run
`git commit`** — commits are split by hand, and they carry no co-author trailer. Leave your own work
staged too (`git add -A` at the end of a wave, no commit).

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
| 6 | The SDK's missing pieces | not started |
| 7 | Documentation and close | not started |

Measured at the end of the audit pass, not remembered:

- **14 projects** under `Src/`, **17** under `Tests/`.
- **3285 tests**, 0 failing, 0 skipped (`dotnet test --solution AppTemplate.sln`).
- **132 architecture rules**, all passing — two more than wave 4 left, both added because the split
  created the gap they close: a host must install the core pipeline before any middleware of its
  own, and an SDK project without the ASP.NET framework reference may not acquire it from a package.
- Five SDK projects declare `IsPackable`: `Domain.Core`, `Application.Core`, `Application.Auth`,
  `Presentation.Core`, `Api.Core`. All five `dotnet pack` cleanly.
- Both container images build.
- `dotnet run Tools/Tasks.cs verify` and `dotnet format --verify-no-changes` both clean.

### The five SDK projects as they stand

Each carries: `IsPackable`, `PackageId`, `Description`, `Authors`, **no version**;
`Microsoft.CodeAnalysis.PublicApiAnalyzers` with `PublicAPI.Shipped.txt` and
`PublicAPI.Unshipped.txt`; `CS1591` re-enabled via `<NoWarn>$(NoWarn.Replace('CS1591', ''))</NoWarn>`
so every public member is documented; and an `InternalsVisibleTo` naming exactly its own mirror.

Public API baselines were generated mechanically from the analyser's own `RS0016` output — 31, 322,
1260, 28 and 134 symbols respectively. **Regenerate the same way** rather than hand-editing: build
the project, collect the symbol from each `error RS0016`, sort, write with a `#nullable enable`
first line. `RS0017` is the other direction and matters as much: it names a symbol the baseline
holds and the code no longer has, so a removal is as explicit a diff as an addition.

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

## What is left

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

### What is still open

### Wave 6 — the SDK's missing pieces

`docs/plans/SDK-SPLIT-BLAST-RADIUS.md` has the verified list. Scheduled: `PeriodicJob`
(`Common/Jobs/` in `Presentation.Core`, which is **why that project has four `Common/` folders today
and not five**), a public email-templating surface, a cache port with one adapter, and a reusable
test kit. Two pre-existing defects get fixed while factoring the three worker loops rather than
reproduced — the maintenance loop neither counts nor logs a disabled purge, and the maintenance and
reminder loops do not run their shutdown log when the stop lands mid-iteration. Nine behavioural
divergences between the loops must be **preserved**, not unified; BLAST-RADIUS names them.

### Wave 7 — documentation and close

The five docs the path gate checks, a "what this SDK does not do" section, a **re-measured**
`coverage.minimum`, and `CHANGELOG.md`.

### Roughly 126 lines of measured, agnostic duplication — offered to the owner, not yet scheduled

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
`PublicAPI` baselines, which is the whole of its cost. **The owner has been shown this table and has
not scheduled it; do not start it unasked.**

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
  `ContainerCompositionTests.RemovingAuthentication_IsHeldUpByTwoInfrastructureCouplings_NotByTheApplicationLayer`.
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
- **Comments: minimum, short, and never about the repository's own history.** No "was", "used to",
  "previously", "moved from", "now that", and no mention of this split.
  `Tools/CheckNarrativeComments.cs` fails the build on those.
- `Tests/` is a 1:1 mirror of `Src/`. Each `InternalsVisibleTo` names exactly one assembly — its own
  mirror.
- **Never `git commit`.** Leave work staged. The owner splits the commits by hand, and they carry
  no co-author trailer.
- At most **three agents in parallel**, on disjoint scopes. Git worktrees are unusable: nothing is
  committed, so an isolated checkout would be empty.

## Open questions — ask, do not assume

The repository owner has asked explicitly for no invention and no silent assumption. Four of the
seven are closed; **2, 3, 5, 6 and 7 below are the live ones** — 2 and 3 answered, the rest still
waiting. Ask; do not pick.

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
5. **Two pre-existing defects the docs carry**, reported and not fixed, both wave-7 candidates:
   `docs/REMOVING-THE-EXAMPLE-FEATURES.md` says "the ten unit and architecture test projects" (there
   are thirteen non-integration ones) and cites measurements taken against a different tree ("2618
   passing", "three rounds of `dotnet build`"); and `docs/CONFIGURATION.md`'s `Localization` section
   advises setting `CultureInfo.CurrentUICulture`, which `CurrentLanguage`'s own remarks explain is
   impossible here because the repository builds with `InvariantGlobalization=true`.
6. **`docs/ARCHITECTURE.md` contains one sentence beginning "There was previously a…"**, which
   violates the repository's own no-history rule. Pre-existing. Fix in wave 7, or leave?
7. **Markdown BOM is inconsistent** — seven files carry one, five do not, and `.editorconfig`
   prescribes a BOM for `[*.cs]` only. Nothing gates it. Normalise, or leave?

Anything not on this list and not settled by the four plan documents is also a question. Ask it.

# Splitting the template — plan of record

**Status:** agreed, not started. **Decided:** 2026-09-07.

This directory is exempt from `Tools/CheckDocPaths.cs` (see its `ExemptDirectoryNames`): a plan
cites the tree it intends to create, so its paths are claims about a future tree.

Read this document for *what happens in what order*. Three companions carry the rest.

| Document | Answers |
|---|---|
| `docs/plans/SDK-SPLIT-TARGET.md` | What each project is: path, name, folders, namespaces, references, packaging |
| `docs/plans/SDK-SPLIT-DECISIONS.md` | Why, including every option that was rejected and its reason |
| `docs/plans/SDK-SPLIT-BLAST-RADIUS.md` | What else has to change: rules, tests, tooling, docs, and the SDK's known gaps |

**Vocabulary.** This repository is a **template**. What the waves below carve out is the part of it
written to package grade: self-contained, ignorant of the application consuming it, a public
surface that changes on purpose. It is not distributed as packages and carries no version, so
wherever these documents say "SDK" they mean that discipline — decision 38.

## The goal, in one paragraph

Five new projects under `Src/` and their five mirrors under `Tests/`, so that what is not business
code lives behind a deliberate public surface and a derived project writes only its own features.
The five are written **as if they were NuGet packages** — self-contained, no knowledge of the
application that consumes them, a public surface that changes only on purpose — and consumed by
`ProjectReference`. They are vendored, not published: a derived project copies them and is free to
tune them, which is why they carry no version of their own.

## Preconditions

1. **A clean `dotnet restore` first.** `Microsoft.Extensions.Http.Resilience` is declared in
   `Directory.Packages.props` but is absent from the local NuGet cache, and both `obj/` trees
   predate it. Restore before splitting, so the first restore error is not blamed on the split.
2. **Every new `.cs` file needs a UTF-8 BOM** (`.editorconfig` sets `charset = utf-8-bom` for
   `[*.cs]`). `EnforceCodeStyleInBuild` plus `TreatWarningsAsErrors` turn a missing one into a
   build error whose message does not say so.
3. **Do not touch `dotnet_diagnostic.CA1515.severity = none` in `.editorconfig`.** It is what makes
   every deliberate `public` type legal here; without it the split cannot compile.

## Waves

Sequential. Each wave ends green — build, full test run, gates, format — before the next starts.

### Wave 0 — Prepare the harness. Nothing moves.

The hard prerequisite. `ArchitectureAssemblies.Anchor` throws when a type it anchors on changes
assembly, and that exception runs in a type initializer: the first moved file fails 24 rule files at
once. So the harness learns to describe N projects *before* anything moves.

- Rewrite the three shared fixtures to read the project graph instead of naming two projects:
  `ArchitectureAssemblies`, `SourceDeclarations`, `ApplicationPorts`.
- `ProjectReferenceGraph.IsHost` becomes "has a `Program.cs` at its root" instead of "sits under
  `Src/Presentation`", and the four rules that lean on it follow.
- Amend `LayoutConventionTests.ModuleFileName` to accept two forms, and generalise the vocabulary
  anti-hole rule from infrastructure modules to every project on disk.
- Add the self-test fixture for the `docs/plans/` exemption added to `Tools/CheckDocPaths.cs`.
- Fix the two self-test failures that predate this work, in the same file. `git check-ignore --stdin`
  echoes each path back **exactly as it was given**, and C-quotes the reply when it contains a
  backslash, so a comparison against a back-slashed Windows path never matches and nothing is ever
  recognised as ignored: the documented property "the gate reads what git tracks" does not hold
  there. Linux agrees with itself, which is why CI is green and only a local run shows it. Normalise
  the separators on the way *in* as well as out, so git is asked and answers in one shape.
  `Tools/Tasks.cs verify` does run `--self-test` — through `Gates(repoRoot)` — so `verify` is red
  on Windows today and green on the Linux runner, which is what kept the two failures unnoticed.
- Make `SearchTerm.Create` public (see the decisions document).
- Downgrade the `<see cref>` that will stop resolving across a future boundary: the refresh-token
  purge use case under `Src/Application/AppTemplate.Application/Features/Maintenance/`, whose
  reference to the idempotency purge is **relative** and breaks when the two use cases land in
  different projects. `TodoListResponseMapping.cs` needs nothing: its two references and the file
  itself all stay in `AppTemplate.Api`.
- Add the package-grade guardrails as *rules only*, with no project to apply them to yet: the
  project-graph rule ("an SDK project references only SDK projects"), the `IsPackable` metadata
  convention, and the CI `dotnet pack` job.

### Wave 1 — `AppTemplate.Domain.Core` (7 files)

The smallest one, chosen to exercise the whole mechanism end to end: project, prefix sweep, the
reference from `AppTemplate.Domain`, `AppTemplate.sln` plus a guid in
`.template.config/template.json`, the `.editorconfig` scoped section for `DomainException.cs`, the
`COPY` list of both Dockerfiles, the anchors, the mirror test project, and the tree in
`CONTRIBUTING.md` plus the table and diagram in `docs/ARCHITECTURE.md`. Also the first
`PublicAPI.txt` baseline and the first `dotnet pack` run.

### Wave 2 — `AppTemplate.Application.Core` (40 files + one use case)

725 namespace occurrences, the two `.editorconfig` scoped sections for `Error.cs` and `Result.cs`,
the registration helpers moved down, its own `InternalsVisibleTo`, and
`Features/Maintenance/UseCases/Commands/PurgeExpiredIdempotencyKeys/`.

### Wave 3 — `AppTemplate.Application.Auth` (169 files) and real optionality

Auth depends on nothing but `Application.Core` — no domain type, no other feature — and its single
inbound edge disappears when `Features/Maintenance` is dissolved. Then: `AddAuthApplication`,
opt-in registration per business feature, both `Program.cs` files, the five compositions in
`HostComposition`, and the replacement of the rule that asserts no module is optional by the test
that is missing today: the container builds *without* Auth.

**The failure mode to watch:** extracting Auth breaks nothing at compile time and everything at
boot. `ApplicationModule` scans its own assembly, so roughly 25 Auth use cases and 20 Auth
validators stop being registered, and `ValidateOnBuild` reports it as a startup failure.

### Wave 4 — `AppTemplate.Presentation.Core`

Five subjects only: outbound HTTP, localisation, telemetry, the "no caller" identity, and
`PeriodicJob`. Merging the three twin pairs is design work, not a file move: two of them share a
type name across the two hosts, so one configuration section and five test files have to be
reworked. Telemetry needs an explicit service name, and the diagnostics names must be supplied by
the host — otherwise this project would have to reference the host that declares them.

### Wave 5 — `AppTemplate.Api.Core`

Nearly all of `Api/Common`, each options type staying with its validator and its `AddApiXxx()`.
`UseCorePipeline()` stays monolithic — the six ordering constraints in the pipeline are coupled
pairwise, so there is no safe extension point in the middle. `AddCoreHealthChecks()` returns the
builder so a derived project can chain its own `DbContext` check. Two leaks get fixed here: Scalar
is hard-coded in the security-header policy, and a login route is written into the OpenAPI
transformer.

### Wave 6 — The missing pieces

`PeriodicJob` as a loop primitive (decision 33), `AppTemplate.Infrastructure.Core` holding the
email-template engine taken from its two copies (decision 37), and an `ICache` port with one
`HybridCache` adapter whose consumer already exists (decision 35). Two pre-existing defects
surface while factoring the loops and get fixed rather than reproduced: the maintenance loop
neither counts nor logs a disabled purge, and its shutdown log does not run when the stop lands
mid-iteration. Decision 34 says what "preserve the nine divergences" means beside that.

The reusable test kit is **not** in this wave: decision 36 withdraws it.

### Wave 7 — Documentation and close

Then, as a separate chantier: `docs/plans/AUTH-SEPARATION.md`, and the comment-convention
cleanup pass recorded in the handoff.

The five docs the path gate checks, the "what this SDK does not do" section, a re-measured
`coverage.minimum`, and `CHANGELOG.md`.

## Exit gate for every wave

```
dotnet build AppTemplate.sln
dotnet run Tools/Tasks.cs verify
dotnet run Tools/Tasks.cs format-fix
dotnet test --solution AppTemplate.sln
dotnet pack <each SDK project>
docker build -f Src/Presentation/AppTemplate.Api/Dockerfile .
docker build -f Src/Presentation/AppTemplate.Worker/Dockerfile .
```

The Worker image is built by hand here on purpose: `.github/workflows/ci.yml` builds only the API
image, so a stale `COPY` list on the Worker side would otherwise surface at release time.

## Working method

- Waves are sequential; inside a wave, up to three agents in parallel on disjoint scopes —
  typically `Src/` plus the sweep, `Tests/`, and packaging plus docs.
- Git worktrees are unusable here: nothing is committed, so an isolated checkout would be empty.
- Everything is left **staged, never committed**. Commits are split by hand.
- Every namespace substitution is anchored on the full `AppTemplate.Application.` prefix. Anchoring
  on a shorter segment corrupts infrastructure namespaces that share it —
  `AppTemplate.Infrastructure.Persistence.Common.Idempotency` and
  `AppTemplate.Infrastructure.InMemory.Features.Auth` are the two that would break silently.

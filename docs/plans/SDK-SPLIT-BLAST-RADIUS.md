# Blast radius, and the SDK's known gaps

Companion to `docs/plans/SDK-SPLIT-PLAN.md`. Two halves: everything outside `Src/` that the split
forces, and the list of what the SDK still will not do.

Volume: about 336 files, roughly 240 of them mandatory edits. `Tools/` needs none — it discovers
from disk — and the CI workflows need one comment.

## Architecture rules — 19 of 33 files

### The five that break loudest

| Rule | What breaks | Fix |
|---|---|---|
| `Fixtures/ArchitectureAssemblies.cs` | `Anchor` throws when `IAggregateRoot` changes assembly, inside a type initializer: **24 rule files fail at once** | Derive the assembly list from the project graph; add five anchors and five namespace constants. Do this in wave 0 |
| `Fixtures/SourceDeclarations.cs` | Two hard-coded roots and two literal root namespaces. The Auth use cases and every cross-cutting port leave the population; the guard does not notice because feature ports remain | Roots become a list of (path, root namespace) read from the project graph |
| `Fixtures/ApplicationPorts.cs` | The cross-cutting prefix matches nothing after the rename, so `ApplicationPorts.All` collapses and four rules go vacuous | Prefixes become a list; the assembly set too |
| `Rules/ModuleDependencyTests.cs` | `Application_ReferencesOnlyTheDomain` and `Domain_ReferencesNoProject` fail by construction. `IsHost` counts an SDK project under `Src/Presentation` as a host, so `OnlyAHost_ReferencesInfrastructureModules` fails its non-vacuity check | Rewrite both reference rules; `IsHost` becomes "has a `Program.cs`" |
| `Rules/LayoutConventionTests.cs` | Two hand-maintained vocabularies keyed by project path; `checkedProjects.ShouldBe(...)` fails as soon as a `Common/` folder disappears; `ModuleFileName` demands three files named `CoreModule.cs` | Add the entries, amend `ModuleFileName`, generalise the anti-hole rule to every project on disk |

### The five that would stay green while checking less

This is the failure mode the whole rule suite exists to prevent, so each needs an explicit fix.

- `Rules/UseCaseConventionTests.cs` — two regexes anchored on the business features namespace, and
  a single-assembly population. Roughly 40 Auth use cases leave both sides at once, so the
  divergence check does not fire.
- `Rules/PortConventionTests.cs` — same shape, plus a cross-cutting namespace constant that matches
  nothing.
- `Composition/ContainerCompositionTests.cs` — "every use case in the application assembly" reads
  one assembly.
- `Rules/LayoutConventionTests.cs` — the new projects are in no vocabulary, and the guard only
  catches a listed project *missing* from disk, never the reverse.
- `Rules/ObservabilityRegistrationTests.cs` — it iterates hosts and reads each host's own
  observability folder; once the extension is shared, every meter left in a host reads as an
  offender while the count stays satisfied.

### The rest

| Rule | Verdict |
|---|---|
| `Rules/LayerDependencyTests.cs` | Breaks. Forbidden lists are namespace *prefixes*, so `AppTemplate.Application` also matches `Application.Core` and `.Auth`. The manifest rule asserts the business project names the domain assembly, which stops being true once the primitives move. Both rules must be duplicated for the new projects |
| `Rules/AdapterVisibilityTests.cs` | Breaks. Expects a port's declaring assembly to be the one application assembly |
| `Rules/HttpSurfaceTests.cs` | Breaks. Walks one project directory; its list of anonymous endpoints all live in Auth controllers. Widen the walk to the three presentation projects |
| `Rules/CollectionContractTests.cs` | Goes vacuous — the records it counts move to `Application.Core` |
| `Rules/DomainEventTests.cs`, `Rules/DomainModelTests.cs` | Break on `using` directives only |
| `Composition/HostComposition.cs` | Five compositions to edit, plus four doc comments naming moved types |
| `Composition/SharedInstanceRegistrationTests.cs` | Breaks on one `using` |
| `Rules/BackgroundWorkTests.cs` | Hard-coded application features path; its lease rule needs the use case to stay where it is |
| `Rules/TemplatePackagingTests.cs` | Breaks until the new guids are in the manifest — this is the rule that guards that list |
| `Rules/FeatureFolderVocabularyTests.cs` | Passes; the walk is recursive and adapts by itself |
| `Rules/ConfigurationSurfaceTests.cs`, `Rules/DeploymentConfigurationTests.cs` | Pass; they walk all of `Src/` generically |
| `Rules/CoverageFloorTests.cs`, `Rules/PersistenceModelTests.cs`, `Rules/EmailTemplateCoverageTests.cs`, `Rules/StorageShapeTests.cs`, `Rules/ExternalKeyTests.cs` | Untouched |

Anti-vacuity thresholds to raise with the counts: at least 9 projects becomes 14, at least 5
infrastructure modules stays, and the file and folder floors should be raised even though the
totals only grow.

## Test projects

- **210 of 387 test files** need namespace edits: 143 name the application cross-cutting namespace,
  40 the domain one, 39 the API one, 7 the worker one. Another 83 name Auth.
- **Five mirror projects created**, roughly 60 test files moved.
- **Three `InternalsVisibleTo` grants split in two**, one each from the application, API and worker
  projects. Each new grant targets exactly one assembly — its own mirror — which is the principle
  the existing comments state. The comment justifying each grant moves with the types it describes,
  except the worker's, which stays with the background services.
- `Tests/Presentation/AppTemplate.Api.UnitTests/Conventions/ControllerContractTests.cs` needs
  rework: it takes "the API assembly" and "the application assembly" as single assemblies and
  compares by assembly identity, so a DTO leaking from Auth would stop being detected.
- `Tests/Architecture/AppTemplate.Architecture.Tests` must reference all five new projects, or the
  anchors fail and about twenty rules check nothing.

## Tooling, CI, packaging

| Item | Work |
|---|---|
| `Tools/` (6 files) | **None.** Everything discovers from disk. `Tools/CheckDocPaths.cs` gained the `docs/plans/` exemption; its self-test fixture is a wave-0 item |
| `.github/workflows/ci.yml` | One comment naming seven instrumented assemblies. Everything else is glob or discovery driven. It is also the job that catches a stale Dockerfile `COPY` list — for the API image only |
| `.github/workflows/release.yml` | None |
| Both Dockerfiles | The explicit `COPY` list per `.csproj`: 7 entries become 12 for the API and 11 for the Worker. The Worker image is not built in CI, so add it to the local gate |
| `AppTemplate.sln` | 5 to 13 new guids, 2 lines and 12 configuration lines per project, plus nesting entries |
| `.template.config/template.json` | The same guids, added to the regeneration list. `sourceName` already renames anything prefixed `AppTemplate.` |
| `.editorconfig` | Three scoped sections follow their files — `Error.cs`, `Result.cs`, `DomainException.cs`. Better: rewrite them as path-insensitive globs so they survive this split and the next. A section whose file no longer exists produces no diagnostic; the suppression simply stops applying and the rule becomes an error at the new path |
| `coverage.minimum` | The floor is unchanged; the prose ranking assemblies by coverage becomes false and no test checks it |
| `coverage.runsettings`, `docker-compose.yml`, `deploy/` | No functional change; about 13 lines of prose |

## Documentation

Five of six docs cite paths the gate checks.

- `docs/CONFIGURATION.md` — 12 checked path citations into the API and Worker common folders, plus
  the "who reads what" table. Do not touch the section heading format: a rule parses it.
- `docs/REMOVING-THE-EXAMPLE-FEATURES.md` — the deletion procedure is a table of paths, roughly 19
  of them, plus a "what to edit" section of 10 files. Removal gets *simpler* after the split, which
  is worth documenting; the Auth removal caveats from the decisions document belong here too.
- `docs/ARCHITECTURE.md` — the four-layer table and the Mermaid diagram go from 4 nodes to 9, and
  the 14-port table changes the declaring project on 12 rows.
- `docs/ADDING-A-FEATURE.md` — states where a feature is created; that instruction changes.
- `docs/DEPLOYMENT.md` — one checked path.
- Outside `docs/`: `CONTRIBUTING.md`'s canonical tree (23 lines, cited by three rule failure
  messages) and two trees in `README.md`.

Also: about 30 references written as `` `path/to/file.cs` `` inside `<c>` tags rather than
`<see cref>`. No compiler checks them, and they name exactly the files being moved. They need a
manual sweep, listed per wave.

## Known gaps of the SDK

Verified absent, not assumed. The first four are scheduled in wave 6; the rest get a "what this SDK
does not do" section in `docs/ARCHITECTURE.md` with the intended extension point for each.

### Scheduled

1. **Recurring work has no abstraction.** The three worker loops are 507 lines that each re-do
   timing, DI scope, instruments and error handling — including an eleven-line wait helper that is
   byte-identical three times over, doc comment included. Nine real behavioural divergences must be
   preserved rather than unified: timer topology, scope granularity, the shutdown path, the disabled
   flag treatment, the volume counter shape, where the enabled test sits, the start-up log contents,
   span and tag names, and log vocabulary.
2. **Email templating is locked inside the identity module.** Multilingual rendering from embedded
   resources already exists; the factory is `internal`, so a derived project cannot render its own
   mail through the SDK.
3. **No cache at all** — no distributed cache, no output caching, no hybrid cache.
4. **The test fixtures are not reusable.** The API factory and the Testcontainers fixtures live
   inside the integration test project, so a derived project copies them.

### Documented as limits

5. **Authorization is role-based with one role.** No permissions, no policy provider, and no tenant
   notion anywhere.
6. **No machine-to-machine authentication** — no API keys, no client credentials flow.
7. **No transactional outbox.** Domain events dispatch in-process after commit, so at-most-once. No
   reliable external integration.
8. **No message bus or queue port.** The Worker polls the database under a Postgres lease.
9. **Minimal APIs are unsupported.** Everything is MVC, and the idempotency filter is an action
   filter, so idempotency, ETags and the result-to-response mapping do not apply to a minimal
   endpoint.
10. No SMS or push notifications, no feature flags, no business audit log (audit columns are not a
    log), no generic soft delete.
11. **No client contracts package.** A future desktop front end talking HTTP needs the request and
    response types or a generated client; generating from the OpenAPI document is the preferred
    route, since a shared contracts assembly couples client and server binaries and undercuts the
    API versioning this template maintains.

### Not gaps — already present and solid

RFC 7807 problem details with error codes, ETag and If-Match, idempotency, rate limiting, CORS,
security headers, forwarded headers, request timeouts and size limits, graceful shutdown, liveness
and readiness, full OpenTelemetry with correlated JSON logs, API versioning with one OpenAPI
document per version, localisation, offset **and** cursor pagination, optimistic concurrency, unit
of work with an interceptor pipeline, domain events, a Postgres leader lease, S3 storage with
antivirus inspection, email over SMTP **and** HTTP, JWT with rotating refresh tokens, two-factor
authentication and external providers, composable in-memory doubles, 33 architecture rules, and a
coverage gate.

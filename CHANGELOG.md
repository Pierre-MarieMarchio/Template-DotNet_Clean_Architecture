# Changelog

All notable changes to this template are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

**What counts as notable in a template**, since it is cloned rather than referenced: anything that
changes the *generated* project — a layer's shape, a convention the architecture tests enforce, a
configuration key, a default that affects security or deployment. A refactor invisible to someone
starting a new project from this repository does not belong here.

**What "breaking" means here**: `Src/` is a starting point, not an API, so it cannot break a caller.
A major version signals that adopting the change in an existing project derived from this template
requires manual work — a renamed configuration section, a moved namespace, a new mandatory setting.

## [Unreleased]

Nothing is released yet. The first tag will publish `1.0.0`, and
`.github/workflows/release.yml` builds the image and the migration bundle from it.

### Added

- **Packaged as a `dotnet new` template.** `dotnet new install <path>` followed by
  `dotnet new cleanarch-webapi -n Your.Project` generates a solution under your own
  name, namespace, and Compose/Docker identifiers — see the README's "Using this as
  a `dotnet new` template" section and `docs/adr/0014`. `.github/workflows/ci.yml`
  gained a `template` job that installs, generates under a different name, builds
  and tests the result on every push.
- `docs/ADDING-A-FEATURE.md`, a full walkthrough of the vertical `CONTRIBUTING.md`'s
  "Adding a feature" section summarises — aggregate → EF model → mapper → tracker →
  store → use case (with its named interface) → controller → tests → migration —
  plus what to remove by hand if you do not want the `TodoLists` sample.
- **Conditional requests on the `TodoList` aggregate.** Every read of a list or item publishes a
  strong, opaque `ETag`; every write honours `If-Match`, refusing a stale, malformed or
  unrecognised version with `412`, and `If-Match: *` against a missing or someone-else's list also
  answers `412` rather than `404`. A repeated `GET` naming the current `ETag` in `If-None-Match`
  answers `304` with no body. New `Concurrency:IfMatch` section (`Optional` by default; `Required`
  refuses a write with no `If-Match` at all with `428`) — see `docs/adr/0013`.
- `ReverseProxy` configuration section (`Enabled`, `KnownProxies`, `KnownNetworks`, `ForwardLimit`)
  driving `UseForwardedHeaders`. Off by default, and the validator **refuses to start** when it is
  enabled with an empty trust set — because ASP.NET Core treats two empty lists as "trust every
  caller", which would let a client choose its own rate-limit partition.
- `TodoItem.MaxTags`, enforced by the aggregate and surfaced as a 400 by the validator, bounding
  per-item tag growth.
- `IDomainEventSource.Restore`, so events survive a failed save and a retry publishes them.
- `Tests/Infrastructure/AppTemplate.Infrastructure.Identity.UnitTests`, including a real two-context race
  against PostgreSQL proving refresh-token rotation is single-use.
- A coverage floor in `coverage.minimum`, read by CI and `tasks.ps1` from the same file. Set from
  measurement (86.53% of lines over 974 tests), not invented, and the file records the measurements
  it was derived from.
- The ADR index is now checked against the records on disk: a new record with no row in
  `docs/adr/README.md`, a row linking a record that was deleted, or two records sharing a number all
  fail `./tasks.ps1 hygiene`. An unindexed record is invisible to anyone starting from the index, and
  nothing else notices.
- `tasks.ps1`, `AppTemplate.Api.http`, `CONTRIBUTING.md`, `SECURITY.md`, this file, and a release workflow
  publishing to GHCR with an SBOM and a signed provenance attestation.
- Dependabot coverage for NuGet, GitHub Actions, Dockerfile **and** `docker-compose.yml` — the last
  needs its own `docker-compose` ecosystem, which `docker` does not cover.
- Test projects for the two modules that had none: `AppTemplate.Infrastructure.Email.UnitTests` (50 tests —
  the SMTP validator that stops a deployment mailing in plaintext was previously untested) and
  `AppTemplate.Infrastructure.InMemory.UnitTests` (30 tests, pinning the recorded-mail ordering every
  integration test leans on).
- CI and `tasks.ps1` discover test projects from disk instead of naming them: a hard-coded list went
  stale once and a project's tests silently did not run. The only exclusion is the architecture
  suite, for the Coverlet reason recorded in the workflow; `Identity.UnitTests` stays out of
  `test -NoIntegration` because its rotation race needs a real database.
- Two repository-hygiene gates, in CI and in `./tasks.ps1 hygiene`:
  `.github/scripts/check-doc-paths.py` asserts every repository path cited in the documentation
  exists, and `.github/scripts/check-workflows.py` catches a dangling `needs:`, an action pinned to
  a mutable tag, a missing `permissions:`, an undefined `$VAR` and a `run:` naming a script that is
  not on disk. Both are the kind of defect no compiler and no test can see.

### Fixed

- **A failure the framework produced carried no `code`.** The API's contract is that a client
  branches on the `code` extension and never on prose, but only errors the application *authors* went
  through `ErrorResults` and got one. A body that is not JSON, a route segment failing its `:guid`
  constraint, a verb no action accepts and an unreadable media type all came back as a bare
  `ProblemDetails` — so the promise was broken on exactly the inputs a client is most likely to get
  wrong by accident. `AddApiProblemDetails` now fills the field in for those, and only for those: a
  `code` already set is never replaced.
- **Every error response from a controller action carried `Content-Type: application/json`
  instead of `application/problem+json`.** `ApiControllerBase`'s class-level
  `[Produces("application/json")]` is a result filter that unconditionally overwrites
  `ObjectResult.ContentTypes`, clobbering the media type `ErrorResults` sets on every failure. The
  attribute is removed; `System.Text.Json` is the only output formatter registered, so a success
  response still negotiates to JSON without it.
- **Rate limiting behind a proxy was ineffective.** The limiter partitions on
  `Connection.RemoteIpAddress`; with no forwarded-headers configuration every caller shared the
  proxy's single 10-request window, so the brute-force protection did not work in production.
- **A failed save destroyed pending domain events.** Catching `ConcurrencyConflictException`,
  reloading and re-saving — the documented recovery for a lost update — published nothing at all.
- **A committed transaction could be reported as failed.** An exception from a domain-event consumer
  escaped the interceptor and surfaced as a commit failure, inviting the caller to retry a write
  that had already been applied. Events after the throwing consumer were also dropped.
- **Refresh-token rotation was not single-use.** The liveness test lived on the `SELECT`, the
  `UPDATE` was keyed on the primary key alone, and the row carried no concurrency token, so two
  simultaneous presentations of one token both succeeded. Revocation is now a single conditional
  `ExecuteUpdate`, and zero affected rows routes to the replay path.
- **A stored row could produce an aggregate violating its own invariants.** `TodoList.Rehydrate`
  appended items without checking the cap, title uniqueness, ownership or nullity.
- **Deleting an aggregate absent from the identity map lost its domain events**, because marking it
  removed was a no-op when it was not already tracked.
- A child-only change rewrote every column of the root row, including its creation stamps.
- `Result<T>.Success` accepted `null`, producing a success carrying `null` under a non-nullable
  declaration — served to a client as a 200 with an empty body.
- Loading a list produced a root × items × tags cartesian product; the read is now a split query.
- The timing-equalisation decoy hash used a different hasher from the one used to verify, so a
  configuration change could silently reopen the account-enumeration timing oracle.
- CI collected coverage over the architecture tests, which fails: NetArchTest resolves types with
  `Type.GetType(name, throwOnError: true)` and that does not work against a Coverlet-instrumented
  assembly. Coverage now excludes that project, and every test still runs exactly once.

### Changed

- **BREAKING: the project prefix is `AppTemplate`, not `CA`.** Every project, namespace, `.sln`/
  `.csproj`/`.http` file name, `InternalsVisibleTo`, and `.editorconfig` path-scoped section is
  renamed — CA.Domain → `AppTemplate.Domain`, CA.Api → `AppTemplate.Api`, CA.sln →
  `AppTemplate.sln`, and so on for every module and test project. The rename exists because
  `dotnet new`'s `sourceName` replaces its token by literal substring match: a `sourceName` of `CA`
  would have rewritten the analyzer rule IDs in `.editorconfig` (`CA1000`, `CA1707`, …) into
  garbage in every generated project. `AppTemplate` cannot collide with a `CAxxxx` id now or in the
  future. **Anyone who forked this repository before this change must merge or reapply that rename
  by hand** — there is no automated migration path, because there is no way to distinguish "this
  repository's own `CA` token" from an adopter's unrelated code that happens to contain `CA` once
  the fork has diverged. See `docs/adr/0014` for the packaging decision this rename made possible.
- Compose's project name and the API image tag are now `app-template`/`app-template-api` (was
  `ca-template`/`ca-api`); `POSTGRES_DB`/`POSTGRES_USER` are now the generic `appdb`/`appuser` (was
  `ca_template`/`ca_app`) — generic on purpose, so a generated project's database credentials do not
  encode its name. See `docs/adr/0014`.
- `Features/<F>/DomainEvents/` is now `Features/<F>/Consumers/` — the folder holds consumers, while
  the Domain's `Events/` holds the event types.
- Feature-level error catalogues moved to `Features/<F>/Errors/`.
- **`IAuthService` is gone.** The six-method façade port is replaced by five narrow capability ports
  (`IUserAccounts`, `IEmailConfirmationTokens`, `IAccessTokenIssuer`, `IRefreshTokenGrants`,
  `IConfirmationEmailComposer`), and the sequencing moved from the Identity adapter into the Auth use
  cases — which is what finally gives `IEmailSender` a consumer in the layer that declares it. The
  refusal reasons are now explicit enums, so collapsing them to one client-facing error is a decision
  a test can watch rather than something invisible inside `SignInManager`.
- Auth request records now live in the file of the use case that accepts them, responses in
  `Features/Auth/Dtos/`, one validator per file — the shape TodoLists already had. `Contracts/`
  is gone.
- The Identity module's folders are `Bearer/`, `Notifications/`, `Options/`, `Templates/`, `Tokens/`
  and `Users/`; `Authentication/` is gone, so composing a mail is no longer filed under
  authentication.
- The two OpenAPI endpoints are mapped `.AllowAnonymous()`. They previously answered **401** in
  Development, because the default-deny fallback policy caught them — the API-reference page has
  never been reachable.
- `ITodoListRepository.GetAsync` no longer defaults its `CancellationToken`, making a dropped token
  visible at the call site.
- `DomainException` is `sealed` and always carries a message.
- Refresh-token primary keys are UUIDv7, matching the domain and giving index locality.

### Removed

- The `ValueObject` base class and its tests. Nothing derived from it: value objects in this
  template are `record`s, which is the idiom it now demonstrates without a second one alongside.
- The `Required by EF Core` parameterless constructors on the domain entities. EF maps persistence
  models, never the domain types, so they were dead code and a persistence concern in the Domain.
- Two unit tests asserting guarantees they could not verify — each configured a substitute and then
  asserted what it had just configured. The real guarantees are pinned where the behaviour lives.

### Known gaps

Stated rather than left to be discovered:

- Within a single domain event, a consumer that throws still prevents later consumers for **that
  event** from running. Events are isolated from one another, consumers of one event are not. This
  is the point at which the mechanism wants an outbox.
- `ConfigureJwtBearerOptions` builds `ProblemDetails` and owns the `auth.required` /
  `auth.forbidden` codes, so the wire format for auth failures has an owner in the infrastructure
  layer as well as in the API.
- `UserManager` and `SignInManager` accept no `CancellationToken`, so cancellation in
  `IUserAccounts` and `IEmailConfirmationTokens` is observed on entry only and cannot be
  propagated to the I/O.

[Unreleased]: https://github.com/OWNER/REPO/compare/main...HEAD

# Target layout — the six package-grade projects

Companion to `docs/plans/SDK-SPLIT-PLAN.md`. This is the document to keep open while implementing.

Every project keeps the shape `CONTRIBUTING.md` states for all four layers —
`Common/<Responsibility>/` plus `Features/<Feature>/<Responsibility>/` — so namespaces follow
folders with no exception, and the sweep is a pure prefix substitution rather than a per-folder
remap.

## The six projects

### `AppTemplate.Domain.Core`

- **Path:** `Src/Domain/AppTemplate.Domain.Core/`
- **Root:** `Common/{Abstractions,Events,Exceptions,Primitives}` — no composition file, exactly as
  `AppTemplate.Domain` has none today
- **Namespaces:** `AppTemplate.Domain.Core.Common.*`
- **References:** none at all, and an architecture rule holds that
- **Content:** the 7 files of `Src/Domain/AppTemplate.Domain/Common/`. None is `internal`, so there
  is nothing to promote

### `AppTemplate.Application.Core`

- **Path:** `Src/Application/AppTemplate.Application.Core/`
- **Root:** `Common/{Collections,Concurrency,Events,Idempotency,Localization,Policies,Ports,Results,UseCases,Validation}`
  plus `Features/Maintenance/UseCases/Commands/PurgeExpiredIdempotencyKeys/`, plus
  `ApplicationCoreModule.cs`
- **Namespaces:** `AppTemplate.Application.Core.Common.*`, `AppTemplate.Application.Core.Features.Maintenance.*`
- **References:** `AppTemplate.Domain.Core` only — never `AppTemplate.Domain`, since the two files
  that need a domain type (`IDomainEventConsumer`, `DomainGuard`) need only the primitives
- **Packages:** `FluentValidation`, `FluentValidation.DependencyInjectionExtensions`,
  `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`
- **Composition:** the three registration helpers moved down from `ApplicationModule` —
  `AddUseCasesFrom`, `AddUseCases`, `AddDomainEventConsumer` — plus
  `AddPurgeExpiredIdempotencyKeys()` kept separate, because `AddApplicationLayer` scans its own
  assembly only and so no longer discovers that use case. There is no umbrella
  `AddApplicationCore()`: see decision 16
- **Promotion:** `SearchTerm.Create` becomes public. `Cursor.Decode`, `CursorKeys` and `SortTerm.Of`
  stay internal — their only consumers are inside this project

### `AppTemplate.Application.Auth`

- **Path:** `Src/Application/AppTemplate.Application.Auth/`
- **Root:** `Features/Auth/{Errors,Policies,Ports,UseCases}` plus `ApplicationAuthModule.cs`
- **Namespaces:** `AppTemplate.Application.Auth.Features.Auth.*`
- **References:** `AppTemplate.Application.Core` only. It names no domain type at all — the identity
  model is not an aggregate here, it lives behind 20 ports
- **Packages:** the four `AppTemplate.Application` carries today, unchanged
- **Composition:** `AddAuthApplication()` — `AddValidatorsFromAssemblyContaining<LoginCommandValidator>`
  (21 validators) and `AddUseCasesFrom` (48 use cases). No service, no domain-event consumer: Auth
  has neither a `Services/` nor a `Consumers/` folder
- **Promotion:** none. Its single `internal`, `PasswordPolicy`, is consumed from within
- **Also receives:** the refresh-token purge use case, dissolved out of `Features/Maintenance`

### `AppTemplate.Presentation.Core`

- **Path:** `Src/Presentation/AppTemplate.Presentation.Core/`
- **Root:** `Common/{Jobs,Localization,Observability,Outbound,Security}` plus `PresentationCoreModule.cs`
- **Namespaces:** `AppTemplate.Presentation.Core.Common.*`
- **References:** `AppTemplate.Application.Core`
- **Packages:** `Microsoft.Extensions.Http.Resilience` (Polly arrives with it),
  `OpenTelemetry.Exporter.OpenTelemetryProtocol`, `OpenTelemetry.Extensions.Hosting`,
  `OpenTelemetry.Instrumentation.Http`, `Microsoft.Extensions.Hosting`, and the
  `Microsoft.Extensions.*.Abstractions` it names directly
- **Explicitly not:** any `FrameworkReference`, and none of the four packages whose nuspec carries
  one — `Scalar.AspNetCore`, `Microsoft.AspNetCore.OpenApi`,
  `OpenTelemetry.Instrumentation.AspNetCore`, and `Asp.Versioning.Mvc` (through its dependency
  `Asp.Versioning.Http`, whose own nuspec is clean, which is what makes that one hard to see).
  Also not `Npgsql.OpenTelemetry`: it pulls the PostgreSQL driver, so the tracer gets an
  `Action<TracerProviderBuilder>` extension point and each host keeps its own `.AddNpgsql()`
- **Content:** outbound HTTP policy (one file instead of two identical ones), the merged
  localisation options, the merged telemetry options with an explicit service name, the "no caller"
  identity, and `PeriodicJob`
- **Promotion:** `OutboundHttpExtensions` and its method — unavoidable, they *are* the composition.
  `PeriodicJob` becomes a public loop primitive, not a base class — decision 33
- **Not here:** the audit actors (they would drag `AppTemplate.Infrastructure.Persistence` into an
  SDK project), and the observability extension may not name a host's diagnostics — the host passes
  the names in

### `AppTemplate.Api.Core`

- **Path:** `Src/Presentation/AppTemplate.Api.Core/`
- **Root:** `Common/{Caching,Concurrency,Contracts,Controllers,Errors,Hosting,Idempotency,Localization,Observability,OpenApi,Security}`
  plus `ApiCoreModule.cs`
- **Namespaces:** `AppTemplate.Api.Core.Common.*`
- **References:** `AppTemplate.Application.Core`, `AppTemplate.Presentation.Core`
- **SDK and framework:** `Microsoft.NET.Sdk` plus an explicit
  `<FrameworkReference Include="Microsoft.AspNetCore.App" />`. Not `Microsoft.NET.Sdk.Web`, which
  would add static web assets, an app host and start-up project behaviour to a class library
- **Packages:** `Microsoft.AspNetCore.OpenApi`, `OpenTelemetry.Instrumentation.AspNetCore`,
  `Asp.Versioning.Mvc` and `Asp.Versioning.Mvc.ApiExplorer`. The last two carry the ASP.NET framework
  reference in their own nuspec, which is legal here and nowhere else in the repository — see
  decision 20. **Not `Scalar.AspNetCore`:** decision 24 takes the API-reference page's policy out of
  this project entirely, so nothing here names that package
- **Forbidden packages — NU1510, and it is an error here:** this is the repository's first non-host
  project to carry the ASP.NET framework reference, so anything the shared framework already
  provides must not be referenced. From `PackageOverrides.txt`: `Microsoft.Extensions.Hosting`,
  `.Http`, `.Options`, `.Configuration*`, `.DependencyInjection*`, `.Logging*`, `.Primitives`,
  `.Diagnostics.HealthChecks` and `.Abstractions`, `System.Threading.RateLimiting`,
  `Microsoft.AspNetCore.Mvc` and `.Core`
- **Content:** 45 of the 48 files in `Api/Common`. Each `*Options` type stays with its `internal`
  validator **and** its `AddApiXxx()` extension — separating them is what would turn zero promotions
  into ten. `Common/Observability/` is the one folder that arrives split rather than whole: the
  ASP.NET instrumentation, the rate-limiting meter, the `/health` filter and the request-log
  middleware come here, and the host keeps a residue for its database instrumentation — decision 23
- **Composition:** `AddApiCore(configuration)` absorbing eight of the nine `AddApiXxx` calls —
  telemetry is the ninth and stays with the host, decision 23 — plus `AddCoreApiVersioning()`,
  `CoreApiVersionGroups()` and `UseCoreDocumentConventions(groupName)`, `AddCoreHealthChecks()`
  returning the builder, `MapCoreHealthEndpoints()` (decision 22), and
  `UseCorePipeline(ResponseContentSecurityPolicy?)`, whose one optional argument is how a host
  states that one path answers under a different policy — decision 24.
  **`AddCoreOpenApiPerVersion` is not among them, and cannot be:** the source generator that turns
  an XML comment into a schema `description` hooks the `AddOpenApi` call through an interceptor, so
  the comments it collects are those of the assembly making that call. The pair above keeps the
  subtle half — the throwaway provider that reads the version list — here, and leaves the four-line
  loop with the host, which is the only place it can produce a documented document. Decision 28
- **Public surface:** public is exactly what a host names, and the compiler settles it type by type —
  decision 26. So the nine `AddApiXxx`/`UseApiXxx` extensions, the option validators, the filter and
  the middleware are `internal`, and the `*Options` types are not: their section names are the
  contract with whoever deploys
- **Promotion:** none of an existing `internal`. Three public members are *new* rather than promoted,
  and each is named here so the count is not read as zero: the `ResponseContentSecurityPolicy`
  delegate, the `/health` path prefix — one constant where three literals stand today — and
  `MapCoreHealthEndpoints`. `IdempotencyFilter` registers itself through `MvcOptions`;
  `GlobalExceptionHandler` folds into the problem-details extension; `CurrentUser` gets an
  `AddApiCurrentUser()` that also absorbs `AddHttpContextAccessor()`
- **Not here:** `DevelopmentDatabaseExtensions` (it names `AppDbContext` and the identity seeder —
  two guaranteed CS1574 and an EF Core dependency), the audit actor, the administrator authorization
  policy (it reads a role name out of the persistence module), the API-reference page's
  content-security policy (decision 24), and the database half of the observability registration
  (decision 23). Those five are what the import graph already says must stay: exactly three of the
  forty-eight files import `AppTemplate.Infrastructure.*`, and they are three of these

### `AppTemplate.Infrastructure.Core`

- **Path:** `Src/Infrastructure/AppTemplate.Infrastructure.Core/`
- **Root:** `Common/{Caching,Templating}` plus `InfrastructureCoreModule.cs`; `Common/Saving`,
  `Common/Idempotency`, `Common/Leases`, `Common/Options` and `Common/Time` join it with the
  separation of authentication — see `docs/plans/AUTH-SEPARATION.md`, decision A3
- **Namespaces:** `AppTemplate.Infrastructure.Core.Common.*`
- **References:** `AppTemplate.Application.Core`
- **Packages:** `Microsoft.Extensions.Caching.Hybrid`
- **Content:** the email-template engine, taken from the two copies decision 37 measures, and
  the `HybridCache` adapter behind `ICache` — decision 35
- **Promotion:** the engine and `RenderedEmail` become public; the engine takes the calling
  assembly, so a module renders its own templates out of its own resources
- **Why the layer needs it:** an infrastructure module may not reference a sibling, so two
  modules needing one mechanism have nowhere to share it — decision 37

## What stays in the hosts

`AppTemplate.Api` keeps `Program.cs`, its feature folders including the Auth controllers,
`Common/Hosting/DevelopmentDatabaseExtensions.cs`, `Common/Security/CurrentUserAuditActor.cs`,
`Common/Security/AuthorizationPolicies.cs`, `Common/Security/ApiReferenceContentSecurityPolicy.cs`
and `Common/Observability/ObservabilityExtensions.cs` — the last two new or reduced to a residue by
decisions 24 and 23 — so its `Common/` holds three folders where it holds eleven today. It keeps the
packages `Microsoft.EntityFrameworkCore.Design`,
`Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore`, `Asp.Versioning.Mvc` (the six
controllers each name its `ApiVersion` attribute), `Scalar.AspNetCore` and `Npgsql.OpenTelemetry`,
and its project-level `NoWarn` for `AV0029;AV0030`. **`Api.Core` carries that suppression too**,
which corrects what this document said before it was implemented: the pair does not target
controllers. `AV0029` fires inside `OpenApiXmlCommentSupport.generated.cs`, which no source-level
directive can reach, and that generated file appears wherever `Microsoft.AspNetCore.OpenApi` is
referenced — which is now both projects. It loses `Asp.Versioning.Mvc.ApiExplorer`: once the per-version loop moves, the host
names nothing from it.

`AppTemplate.Worker` keeps `Program.cs`, its three feature folders with their `*Instruments` and
`*WorkerOptions`, `Common/Security/BackgroundAuditActor.cs`, and the packages
`Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Logging.Console` and `Npgsql.OpenTelemetry`.
It gains a reference to `AppTemplate.Presentation.Core` and loses five package references to it.

## Infrastructure module references after the split

Auth is never transitive: nothing in `Application.Core` or in the business project names it, so a
module that needs it must declare it.

| Module | Application-side references | Change |
|---|---|---|
| `AppTemplate.Infrastructure.Persistence` | `Application.Core`, `AppTemplate.Application` | one addition |
| `AppTemplate.Infrastructure.Auth` | `Application.Core`, `Application.Auth`, `Infrastructure.Persistence` | drops `AppTemplate.Application` entirely — it implements 20 Auth ports and nothing else |
| `AppTemplate.Infrastructure.Email` | `Application.Core`, `Application.Auth`, `AppTemplate.Application` | two additions; the Auth one is mandatory, its reminder notifier resolves a user profile to find an address |
| `AppTemplate.Infrastructure.Storage` | `Application.Core`, `AppTemplate.Application` | one addition, for the `Result` type alone |
| `AppTemplate.Infrastructure.InMemory` | `Application.Core`, `Application.Auth`, `AppTemplate.Application` | two additions; its one Auth double is already registered by a separate public method |

## Packaging metadata, on all five

- `IsPackable=true`, with `PackageId`, `Description` and `Authors`; `dotnet pack` runs in CI and is
  part of every wave's exit gate. It is what proves self-containment: it fails if an SDK project
  references a non-packable project or loses an embedded resource.
- `Microsoft.CodeAnalysis.PublicApiAnalyzers` with a tracked `PublicAPI.Shipped.txt` and
  `PublicAPI.Unshipped.txt` per project, so any change to the public surface is an explicit diff to
  review rather than a side effect.
- `CS1591` re-enabled here only: documentation is required on every public member of the SDK.
- **No version.** These are vendored, not published; a derived project copies and tunes them.
- One architecture rule over the project graph: an SDK project references only SDK projects — never
  a business project, an infrastructure module or a host.

## Mirror test projects

`CONTRIBUTING.md` states that `Tests/` is a 1:1 mirror of `Src/`, so five mirrors are created and
roughly 60 test files move. This is also what keeps each `InternalsVisibleTo` pointing at exactly
one assembly — its own mirror — which is the principle the existing `.csproj` comments state.

| New test project | Receives `InternalsVisibleTo` for |
|---|---|
| `AppTemplate.Domain.Core.UnitTests` | nothing — the project has no `internal` |
| `AppTemplate.Application.Core.UnitTests` | `Cursor.Decode`, `CursorKeys`, `SortTerm.Of` |
| `AppTemplate.Application.Auth.UnitTests` | `PasswordPolicy` |
| `AppTemplate.Presentation.Core.UnitTests` | the merged options validators, the "no caller" identity |
| `AppTemplate.Api.Core.UnitTests` | more than the 15 types counted before decision 26, which makes the nine registration extensions and the middleware internal too. The grant is generated from what fails to compile, not from a list — among them `EntityTagMapping`, `IfMatchPrecondition`, `ProblemDetailsNormaliser`, `IRateLimitCounters`, `IdempotencyFilter` |

`AppTemplate.Application`, `AppTemplate.Api` and `AppTemplate.Worker` each keep their own grant for
what stays behind — the feature mappers, the response mappers, and the three background services.
`DynamicProxyGenAssembly2` is needed by none of the five: the only `internal` interface among them
is deliberately doubled by hand rather than substituted, and every other `internal` class is
`sealed`.

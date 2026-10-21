# Layers and projects

Each layer is one folder under `Src/`, and each folder holds a `.Core` project for the mechanisms
that know no feature, beside the projects that hold the business. A project declares every reference
it uses; nothing reaches it transitively as a favour, so removing a project never takes a mechanism
with it silently.

<!--#if (localAuth)-->
The four `.Core` projects and `AppTemplate.Application.Auth` are marked `IsSdkProject`.
<!--#endif-->
<!--#if (!localAuth)-->
The four `.Core` projects are marked `IsSdkProject`.
<!--#endif-->
They reference only one another (`EverySdkProject_ReferencesOnlySdkProjects`), carry no version and
are meant to be tuned where they stand.

## Domain

| Project | Holds | References |
|---|---|---|
| `AppTemplate.Domain.Core` | `Entity`, `AggregateRoot`, `AuditableAggregateRoot`, `IDomainEvent`, `DomainException`, `UserId`, and the `IAuditable`, `IVersioned` and `IOwnedAggregate` contracts | nothing |
| `AppTemplate.Domain` | each rich feature's aggregate, value objects, events and repository contract | `AppTemplate.Domain.Core` |

The domain takes no package at all. That keeps it testable with no host, no database and no mocking
framework; `TheDomainLayer_DependsOnNothing` holds it.

## Application

| Project | Holds | References |
|---|---|---|
| `AppTemplate.Application.Core` | `Result` and `Error`, the `IUseCase` marker, validation, paging, idempotency, concurrency, ownership, and the cross-cutting ports in `Common/Ports/` | `AppTemplate.Domain.Core` |
| `AppTemplate.Application` | the business features: use cases, feature ports, DTOs, validators, event consumers | `AppTemplate.Domain`, `AppTemplate.Application.Core` |
<!--#if (localAuth)-->
| `AppTemplate.Application.Auth` | six authentication features as use cases over their own ports; it names no aggregate | `AppTemplate.Application.Core` |
<!--#endif-->

<!--#if (anyFeature)-->
`AppTemplate.Application.Core` also holds the use cases no business feature owns: the purge of
expired idempotency keys, and the leased sweep of the data deleted accounts leave behind.
<!--#endif-->
<!--#if (!anyFeature)-->
`AppTemplate.Application.Core` also holds the one use case no business feature owns: the purge of
expired idempotency keys.
<!--#endif-->

<!--#if (localAuth)-->
Nothing in the other two application projects names `AppTemplate.Application.Auth`, so a project that
needs it declares it: the identity module, the in-memory doubles and the host.
`TheApplicationLayer_KnowsNothingOfAuthentication` reads the assembly manifests to hold it, and
`TheApiContainer_BuildsWithoutAuthentication` proves the rest composes without it.
<!--#endif-->

## Infrastructure

`AppTemplate.Infrastructure.Core` holds what several modules need and none owns: the unit of work,
the three save interceptors, the aggregate tracker base, the clock, mail templating, the cache
adapter and the database options. It references `AppTemplate.Application.Core` only, and holds no
database provider: which database a context reaches is its module's choice.

Every other infrastructure project is a module, one per capability. A module references the
application projects whose ports it implements and the layer's `.Core`, never another module
(`InfrastructureModules_ReferenceOnlyTheLayersCoreHorizontally`), and only a host references a
module (`OnlyAHost_ReferencesInfrastructureModules`). The modules themselves are listed in
[Modules and boundaries](modules-and-boundaries.md#the-modules).

## Presentation.Core: host-agnostic

`AppTemplate.Presentation.Core` holds what any host needs whatever its transport:

| Subject | Type |
|---|---|
| One outbound HTTP policy on `IHttpClientFactory`'s defaults | `AddOutboundHttp()` |
| The default language of everything a host writes | `LocalizationOptions` |
| OTLP traces and metrics, named after the host's assembly | `AddObservability` |
| The caller of a process that has no request | `NoCallerCurrentUser` |
| The loop recurring work runs on | `PeriodicJob` |

It has no `FrameworkReference`, no ASP.NET Core package and no database driver, so a CLI, a desktop
client or a second API host composes the same use cases without inheriting ASP.NET or PostgreSQL.
`PortabilityTests` reads the project files and their reference closure and fails if the domain, the
application, the infrastructure or this project reaches the ASP.NET Core shared framework.
<!--#if (localAuth)-->
The one exemption is `AppTemplate.Infrastructure.Auth`, whose JWT bearer handler carries the
framework; the test names it with that reason and fails if the reason stops being true.
<!--#endif-->

A host keeps a thin extension over the shared telemetry registration: the API adds the ASP.NET Core
and Npgsql instrumentation and its own meters, because only it may reference those packages.

## The API host

`AppTemplate.Api` is the one project on the Web SDK. It holds the controllers, the background jobs,
the composition root in `Program.cs`, and under `Common/` the HTTP mechanisms every feature shares.

A wire type is the API's own: each feature declares its requests and responses under `Contracts/`
and maps them by hand in `Contracts/Mapping/`. No application type and no port type is serialised
as it is, so a field added inside the application is never published by that edit alone.

## Composition

Each project exposes one registration class, and each feature one line in `Program.cs`. There is no
call that adds a whole layer: deleting a feature's folder and its line is the whole removal, and a
module stays optional because nothing registers its ports on its behalf.

```csharp
builder.Services.AddOutboundHttp();
<!--#if (todoLists)-->
builder.Services.AddTodoLists();
<!--#endif-->
<!--#if (reminders)-->
builder.Services.AddReminders();
<!--#endif-->
<!--#if (files)-->
builder.Services.AddFiles(builder.Configuration);
<!--#endif-->
<!--#if (notes)-->
builder.Services.AddNotes();
<!--#endif-->
builder.Services.AddPurgeExpiredIdempotencyKeys();
<!--#if (anyFeature)-->
builder.Services.AddDeletedAccountSweep();
<!--#endif-->
builder.Services.AddCacheStore();
builder.Services.AddPersistenceModule(builder.Configuration);
builder.Services.AddEmailModule(builder.Configuration);
<!--#if (files)-->
builder.Services.AddStorageModule(builder.Configuration);
<!--#endif-->
builder.Services.AddControllers();
builder.Services.AddHttpPipeline(builder.Configuration);
builder.Services.AddApiObservability(builder.Configuration);
builder.Services.AddBackgroundJobs(builder.Configuration);
<!--#if (localAuth)-->
builder.Services.AddAuthFeature(builder.Configuration);
<!--#endif-->
<!--#if (externalAuth)-->
builder.Services.AddExternalAuthentication(builder.Configuration);
<!--#endif-->
```

<!--#if (localAuth)-->
`AddAuthFeature` composes the authentication use cases, the identity module, its purge jobs, its
readiness check, the `Administrator` policy and its development migration and seed; a host without
local accounts deletes that line and its folder.
<!--#endif-->
<!--#if (externalAuth)-->
`AddExternalAuthentication` composes the provider's bearer validation, the mapping of its subjects
to owners and the `Administrator` policy.
<!--#endif-->

`TheApiContainer_BuildsUnderStrictValidation` builds the API's container with `ValidateOnBuild` and
`ValidateScopes`, so a missing or mis-scoped registration fails a test rather than a request.

## Decisions

- [0001. Adopt the generated baseline](../decisions/0001-adopt-the-generated-baseline.md): four layers, and a host-agnostic Presentation.Core.
- [0003. Use cases and ports are explicit](../decisions/0003-use-cases-and-ports-are-explicit.md)

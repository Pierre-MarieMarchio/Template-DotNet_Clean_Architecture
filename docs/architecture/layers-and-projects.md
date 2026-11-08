---
type: concept
title: Layers and projects
description: Which projects make up each layer, what each holds, and how a project declares what it references.
---

# Layers and projects

Each layer is one folder under `src/`, and each folder holds a `.Core` project for the mechanisms
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
| `AppTemplate.Application.Core` | `Result` and `Error`, the use-case pipeline and its instruments, validation, paging, idempotency, concurrency, ownership, and the cross-cutting ports in `Common/Ports/` | `AppTemplate.Domain.Core` |
| `AppTemplate.Application` | the business features: use cases as messages and handlers, feature ports, DTOs, validators, event consumers | `AppTemplate.Domain`, `AppTemplate.Application.Core` |
| <!--#if (localAuth)--> |
| `AppTemplate.Application.Auth` | the authentication features as use cases over their own ports; it names no aggregate | `AppTemplate.Application.Core` |
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
| OTLP logs, traces and metrics, named after the host's assembly | `AddObservability` |
| The caller of a process that has no request | `NoCallerCurrentUser` |
| The loop recurring work runs on | `PeriodicJob` |
| The scope a pass runs in, whose ports answer that no one is calling | `CreateBackgroundScope()` |
| <!--#if (externalAuth)--> |
| The provider every host signs callers in with, and the roles read from its tokens | `AuthenticationExternalOptions`, `TokenRoleMapping` |
<!--#endif-->

It has no `FrameworkReference`, no ASP.NET Core package and no database driver, so a CLI, a desktop
client or a second API host composes the same use cases without inheriting ASP.NET or a database provider.
`PortabilityTests` reads the project files and their reference closure and fails if the domain, the
application, the infrastructure or this project reaches the ASP.NET Core shared framework.
<!--#if (localAuth)-->
The one exemption is `AppTemplate.Infrastructure.Auth`, whose JWT bearer handler carries the
framework; the test names it with that reason and fails if the reason stops being true.
<!--#endif-->

<!--#if (api)-->
<!--#if (postgreSql)-->
A host keeps a thin extension over the shared telemetry registration: the API adds the ASP.NET Core
and Npgsql instrumentation and its own meters, because only it may reference those packages.
<!--#endif-->
<!--#if (sqlServer)-->
A host keeps a thin extension over the shared telemetry registration: the API adds the ASP.NET Core
and SqlClient instrumentation and its own meters, because only it may reference those packages.
<!--#endif-->
<!--#if (sqlite)-->
A host keeps a thin extension over the shared telemetry registration: the API adds the ASP.NET Core
instrumentation and its own meters, because only it may reference those packages. SQLite has no
database instrumentation to add.
<!--#endif-->

<!--#endif-->
<!--#if (api)-->
## The API host

<!--#if (!presentationBlazorServer && !presentationMcp)-->
`AppTemplate.Api` is the one project on the Web SDK. It holds the controllers, the background jobs,
<!--#endif-->
<!--#if (presentationBlazorServer || presentationMcp)-->
`AppTemplate.Api` is the host of the HTTP contract. It holds the controllers, the background jobs,
<!--#endif-->
the composition root in `Program.cs`, and under `Common/` the HTTP mechanisms every feature shares.

A wire type is the API's own: each feature declares its requests and responses under `Contracts/`
and maps them by hand in `Contracts/Mapping/`, as [mappings](#mappings) says. No application type and no port
type is serialised
as it is, so a field added inside the application is never published by that edit alone.

<!--#endif-->
<!--#if (presentationBlazorServer)-->
## The portal

`src/Presentation/<Initials>.Portal` is a host: a Blazor Web App that calls the use cases in
its own process. It references the application projects and the modules it composes, and never another
host (`NoHost_ReferencesAnotherHost`); under `Common/` and `Features/` it keeps
its own caller, its sign-in and the adapters a host supplies. [Presentation projects](../features/presentation.md)
describes it.

<!--#endif-->
<!--#if (presentationAvalonia)-->
## The desktop app

`src/Presentation/<Initials>.Desktop` is a desktop client: an Avalonia app that calls the API over
HTTP through the .NET client of `src/Presentation/<Initials>.Presentation.Client`. It references
that client alone (`EveryClient_ReachesTheCoreThroughTheApiAlone`), so it is no host: it composes
no use case and no module. [Presentation projects](../features/presentation.md) describes it.

<!--#endif-->
<!--#if (presentationMcp)-->
## The MCP server

`src/Presentation/<Initials>.Mcp` is a host: an MCP server whose tools call the use cases
in its own process, one use case each. It references the application projects and the modules it
composes, and never another host (`NoHost_ReferencesAnotherHost`); under `Common/`
and `Features/` it keeps its own caller, its authentication, its tools and the adapters a host
supplies. [Presentation projects](../features/presentation.md) describes it.

<!--#endif-->
<!--#if (presentationWpf)-->
## The desktop app

`src/Presentation/<Initials>.Desktop` is a desktop client for Windows: a WPF app that calls the API
over HTTP through the .NET client of `src/Presentation/<Initials>.Presentation.Client`. It
references that client alone (`EveryClient_ReachesTheCoreThroughTheApiAlone`), so it is no host: it
composes no use case and no module. It is developed, run and tested on Windows; other systems build it.
[Presentation projects](../features/presentation.md) describes it.

<!--#endif-->
<!--#if (dotnetClient)-->
## The .NET client

`AppTemplate.Presentation.Client` is the .NET client of the API, beside
`AppTemplate.Presentation.Core`: Kiota writes its `Generated/` folder from the OpenAPI reference,
and `Common/` adds the base address, the resilience handler, the session and the reading of a
problem's `code`. Only a presentation that runs on the user's machine calls it, so it is a
presentation project and not a layer of its own. It references no project, and a presentation that
calls the API through it references it alone (`EveryClient_ReachesTheCoreThroughTheApiAlone`).

A folder of its own beside the four layers would read as a fifth layer, though nothing but a
presentation calls it, and the rule that keeps it from the core names the project, not a folder.
Inside `Presentation.Core`, which references the application layer, it would let a desktop or
WebAssembly app ship the server's code and reach the use cases in process. Types written by hand
would drift from what the API returns, with nothing to say when a change was missed. NSwag's C#
output does not build here and reads its types by reflection, which trimming breaks; Refitter adds a
third-party generator with telemetry on by default and problems parsed by hand. Every file Kiota
writes opens with its `// <auto-generated/>` header and `.editorconfig` marks `**/Generated/**.cs` as
generated, so the analyzers and `dotnet format` leave it alone, and the `#pragma` lines Kiota writes
stay in `Generated/`; `openapi --check` fails when the folder drifts from the OpenAPI reference, as
[the gate](../guides/build-and-ci.md#the-gate) says. A change to the contract is then a compile error
in every .NET client that relied on the old one.

<!--#endif-->
## Mappings

Every mapping is a method written by hand, on the side that produces the type, and a property
reaches the other side only through a line that names it:
<!--#if (api)-->
a response mapping under the API's `Contracts/Mapping/`,
<!--#endif-->
a DTO mapping under the application's `Model/Mapping/`, and a rich feature's row mapper under the
persistence module's `Rows/Mapping/`. A library that pairs members by name, AutoMapper at run time or
Mapperly at compile time, would let a property added on both sides reach the published contract with
no line in the diff, and a row mapper would need attributes to build through the aggregate's factory
and skip the version and the audit stamps, the same knowledge as the hand-written lines in a second
form. `BannedPackageTests` refuses both packages. Mappings cost lines, and a row mapper that forgets a
property loses data without failing anything, so its tests walk the aggregate's type. The choice is
revisited when mappings grow to a share of each change that review no longer reads, or when a
generator can be told to map only the members a mapping names.

## Composition

Each project exposes one registration class, and each feature one line in the `Program.cs` of each
host that composes features. There is no call that adds a whole layer, and no project that composes
a host on another's behalf: deleting a feature's folder and its lines is the whole removal, and a
module stays optional because nothing registers its ports on its behalf.

Where a presentation project runs decides how it reaches the use cases. One that runs on the server
references the application layer and the infrastructure modules it needs and calls the use cases in
its own process. One that runs on the user's machine, a web or desktop app, calls the API through a
client generated from its document and references neither layer. Sending every presentation through
the API would make a server-rendered site carry an HTTP call and a token per user for a process that
could call the use case directly.

Each host composes itself: its feature registrations, its modules, its caller's identity and its
authentication. No host references another (`NoHost_ReferencesAnotherHost`), and
`OnlyAHost_ReferencesInfrastructureModules` treats every project with a `Program.cs` alike, but a
client, which references the .NET client alone. One project composing every host would hold the
feature lines once, and make each host's composition a call into a project that serves several
masters: a host that needs one module less or one adapter more would have to work around it. What
two hosts genuinely share and is no composition, settings that describe a provider or a mapping that
knows no host, goes to `Presentation.Core` while it stays free of ASP.NET Core; the rest each host
keeps. A setting the use cases read is set for every host that composes them, and a host's sign-in
and session belong to it, each host mapping its principal to the same `UserId`. This holds until
several server hosts need the same composition so often that keeping it in step costs more than a
shared project would.
<!--#if (!jobsWorker)-->

Jobs and migrations stay in the API: a second host runs no background job and migrates nothing,
since the methods that do both live in the API, which no other host may reference. Two hosts running
the jobs would run every pass twice, with the lease the only thing between them.
`TheJobs_RunInOneHost` holds that one host alone calls `AddBackgroundJobs`.
<!--#endif-->
<!--#if (jobsWorker && api)-->

The worker is the one host that runs background jobs, and the API the one that migrates: the
methods that do each live in that host, which no other host may reference. Two hosts running the
jobs would run every pass twice, with the lease the only thing between them. `TheJobs_RunInOneHost`
holds that one host alone calls `AddBackgroundJobs`.
<!--#endif-->
<!--#if (jobsWorker && !api)-->

The worker is the one host that runs background jobs: the method that does it lives in the worker,
which no other host may reference. Two hosts running the jobs would run every pass twice, with the
lease the only thing between them. `TheJobs_RunInOneHost` holds that one host alone calls
`AddBackgroundJobs`.
<!--#endif-->

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
var controllers = builder.Services.AddControllers();
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
<!--#if (localAuth || externalAuth)-->

<!--#if (localAuth)-->
`AddAuthFeature` composes the authentication use cases, the identity module, its purge jobs, its
readiness check, the `Administrator` policy and its development migration and seed; a host without
local accounts deletes that line and its folder.
<!--#endif-->
<!--#if (externalAuth)-->
`AddExternalAuthentication` composes the provider's bearer validation, the mapping of its subjects
to owners and the `Administrator` policy.
<!--#endif-->
<!--#endif-->

`TheApiContainer_BuildsUnderStrictValidation` builds the API's container with `ValidateOnBuild` and
`ValidateScopes`, so a missing or mis-scoped registration fails a test rather than a request.

## Decisions

- [0003. Use cases are Mediator messages, and ports are explicit](../decisions/0003-use-cases-are-mediator-messages-and-ports-are-explicit.md)

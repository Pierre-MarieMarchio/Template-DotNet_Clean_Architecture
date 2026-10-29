# 0001. Adopt the generated baseline

Status: Accepted

## Context

This project started with its foundations in place before any feature used them: paging,
idempotency, outbound HTTP, background jobs and their leases, a cache, and the tooling that builds,
tests and ships the code. A foundation is judged by what it makes easier later, not by how many
endpoints use it today. One that nothing uses yet stays until someone decides to remove it, and
that removal is a decision like any other, not a tidy-up.

Every choice below could have gone another way. The team did not debate them one by one: they came
with the project. Recording them as one decision gives each later reversal a row to point at, and
keeps the reasons where the other decisions are.

## Decision

This project adopts the choices in the table. Each row links the page that describes it and states
what changing it costs.

| Choice | Why, in one line | Described in | Changed by |
|---|---|---|---|
| Clean Architecture in four layers, dependencies pointing inward | What may depend on what is the organising rule; `LayerDependencyTests` hold it | [Architecture](../architecture/README.md) | — |
| `Presentation.Core` is host-agnostic: no ASP.NET Core framework reference, no database driver | A CLI, desktop or second API host composes the same use cases; `PortabilityTests` hold it | [Layers and projects](../architecture/layers-and-projects.md#presentationcore-host-agnostic) | — |
| Foundations ship before a feature uses them: paging, idempotency, outbound HTTP, jobs and leases, cache | A feature codes its business, not its plumbing | [Shared mechanisms](../architecture/shared-mechanisms.md) | — |
<!--#if (agentFiles)-->
| Clean comments: the code reads by itself; a comment stays only for what the code cannot say (a reason, an outside constraint, a reference), and one that explains the code calls for reworking the code | A comment that restates the code rots the next time anything moves | [AGENTS.md](../../AGENTS.md#changing-code) | — |
<!--#endif-->
<!--#if (!agentFiles)-->
| Clean comments: the code reads by itself; a comment stays only for what the code cannot say (a reason, an outside constraint, a reference), and one that explains the code calls for reworking the code | A comment that restates the code rots the next time anything moves | [Contributing](../../CONTRIBUTING.md) | — |
<!--#endif-->
| .NET SDK pinned by `global.json`, package versions in `Directory.Packages.props`, warnings as errors | One toolchain on every machine and in CI | [Build and CI](../guides/build-and-ci.md#the-gate) | — |
| Every dependency at an exact version: packages, container images by digest, workflow actions by commit; Dependabot proposes updates in bands and ignores none | A tag or a range moves under you; an ignored dependency is one nobody revisits | [Supply chain](../guides/build-and-ci.md#supply-chain) | — |
| ASP.NET Core MVC controllers, `Asp.Versioning`, `Microsoft.AspNetCore.OpenApi`; Scalar in Development | Attribute-routed controllers carry the conventions; the framework's own generator writes the document | [HTTP API](../architecture/http-api.md#versioning-and-the-openapi-document) | — |
<!--#if (postgreSql)-->
| PostgreSQL through EF Core and Npgsql | One relational store with schemas, advisory locks and `xmin` | [Persistence model](../architecture/persistence-model.md) | — |
<!--#endif-->
<!--#if (sqlServer)-->
| SQL Server through EF Core | One relational store with schemas, application locks, and a version counter the save stamps | [Persistence model](../architecture/persistence-model.md) | — |
<!--#endif-->
<!--#if (sqlite)-->
| SQLite through EF Core, for development | One file, no server to run; a table prefix per feature, in-process locks and a version counter the save stamps | [Persistence model](../architecture/persistence-model.md) | Moving to a server database: [ADR 0021](0021-sqlite-is-a-development-database.md) |
<!--#endif-->
| FluentValidation, run by each use case | Validation is a use case's first line, testable without HTTP | [Request pipeline](../architecture/request-pipeline.md#validation) | — |
<!--#if (localAuth)-->
| Local accounts on ASP.NET Core Identity: JWT access tokens, rotating refresh tokens | The API is its own identity provider; the mode decides which modules exist | [Authentication](../features/authentication.md) | — |
<!--#endif-->
<!--#if (externalAuth)-->
| Tokens from an external identity provider, validated by the API | Credentials stay with the provider; the mode decides which modules exist | [Authentication](../features/authentication.md) | — |
<!--#endif-->
<!--#if (publicApi)-->
| No accounts: a public API | Nothing the API serves belongs to a caller; the mode decides which modules exist | [Authentication](../features/authentication.md) | — |
<!--#endif-->
| `HybridCache`, in process | A shared second level is one `IDistributedCache` registration away, with no caller changed | [Cache](../architecture/shared-mechanisms.md#cache) | — |
| Invariant globalization; the language of an answer is a tag | No ICU in the image; mails are chosen by tag and dates written round-trip | [Language of the answer](../architecture/request-pipeline.md#language-of-the-answer) | — |
| OpenTelemetry over OTLP | Vendor-neutral export, the backend chosen per deployment | [Observability](../features/observability.md) | — |
| Mail over SMTP or Postmark, chosen by configuration; Mailpit locally | One transport, chosen once, with no SDK | [Email](../features/email.md) | — |
<!--#if (files)-->
| An S3-compatible object store; SeaweedFS locally, from the image its own project publishes | MinIO publishes no image of its own; SeaweedFS passes the storage suite unchanged | [Files](../features/files.md#storage) | — |
<!--#endif-->
| Docker Compose for the local stack | One command runs the API and its dependencies | [First run](../getting-started/first-run.md#run-with-compose) | — |
| TLS terminates upstream: HTTP on 8080, no HTTPS redirection, no HSTS; forwarded headers believed only from a named proxy, none named by default | Only the component terminating TLS knows the domain's commitments | [Behind a proxy](../guides/deployment.md#behind-a-proxy) | — |
| No migration ships with the project: `bootstrap` creates each context's initial migration, and the team commits it | A migration written for every shape the project could take is conditioned by hand and drifts; EF writes the first one from the model and the database this project has | [The initial migration](../guides/migrations.md#the-initial-migration) | — |
<!--#if (serverDatabase)-->
| Migrations applied at release: one bundle per context, built from the migrations the image's commit carries, run by a principal with DDL rights; the API migrates itself in Development only | A process never alters its own schema in production | [Migrations at release](../guides/deployment.md#migrations-at-release) | — |
<!--#endif-->
<!--#if (sqlite)-->
| The API migrates itself in Development, and refuses to start anywhere else | A SQLite file is a development database; no release applies migrations to it | [Migrations](../guides/migrations.md) | Moving to a server database: [ADR 0021](0021-sqlite-is-a-development-database.md) |
<!--#endif-->
| The API image on alpine, running unprivileged | The healthcheck needs `wget`, which the chiseled images lack | [The image](../guides/deployment.md#the-image) | — |
<!--#if (postgreSql)-->
| xUnit v3, Shouldly, NSubstitute, Testcontainers, NetArchTest | Integration tests run on a real PostgreSQL; the architecture is held by tests | [Testing](../guides/testing.md) | — |
<!--#endif-->
<!--#if (sqlServer)-->
| xUnit v3, Shouldly, NSubstitute, Testcontainers, NetArchTest | Integration tests run on a real SQL Server; the architecture is held by tests | [Testing](../guides/testing.md) | — |
<!--#endif-->
<!--#if (sqlite)-->
| xUnit v3, Shouldly, NSubstitute, NetArchTest | Integration tests run on a real SQLite file; the architecture is held by tests | [Testing](../guides/testing.md) | — |
<!--#endif-->
<!--#if (ci)-->
| GitHub Actions | The gate runs where the repository lives | [Workflows](../guides/build-and-ci.md#workflows) | — |
| CodeQL code scanning beside the gate, weekly and on every change to `main` | Taint paths a test does not walk, reported where GitHub shows alerts | [Workflows](../guides/build-and-ci.md#workflows) | — |
<!--#endif-->
<!--#if (sonar)-->
| Sonar for static analysis and coverage | Issues and coverage read in one place, locally as in CI | [Sonar](../guides/build-and-ci.md#sonar) | — |
<!--#endif-->
<!--#if (anyWeb)-->
| One pnpm workspace under `Src/Web`, Node and pnpm pinned | Apps and packages share one lockfile; nothing in the .NET build needs Node | [Web front end](../features/web-frontend.md) | — |
<!--#endif-->
<!--#if (presentationBlazorServer)-->
| A Blazor Web App with server interactivity beside the API, its pages from `dotnet new blazor` at bootstrap, composing the use cases in process | A server-rendered site needs no client of the API and no token per user | [Presentation project](../features/presentation.md) | — |
<!--#endif-->
<!--#if (presentationAvalonia)-->
| An Avalonia desktop app beside the API, its views from `dotnet new avalonia.mvvm` at bootstrap, calling the API through the .NET client | A desktop app runs on the user's machine, where only the HTTP contract reaches the use cases | [Presentation project](../features/presentation.md) | [0022](0022-a-presentation-project-reaches-the-core-through-the-api-or-the-application-layer.md) |
<!--#endif-->
<!--#if (presentationWpf)-->
| A WPF desktop app for Windows beside the API, in the shape of `dotnet new wpf`, its view models in a `.Core` project that builds and is tested on every system, calling the API through the .NET client | A desktop app runs on the user's machine, where only the HTTP contract reaches the use cases | [Presentation project](../features/presentation.md) | [0022](0022-a-presentation-project-reaches-the-core-through-the-api-or-the-application-layer.md) |
<!--#endif-->

## Consequences

- Reversing or replacing a row is a product ADR, numbered from 1000, that names the row in its
  context. The row's "Changed by" cell then links that ADR. This ADR stays Accepted, so the table
  shows at a glance which inherited choices still hold.
- Each row's page states what changing it costs: the tests that hold it, the files that move, the
  release steps that depend on it.
- What this project was generated without is absent from the table and from the code, not switched
  off. Adding it later is a change with its own ADR when it involves a choice.
- A choice made after generation that touches no row is an ADR of its own, not a row here.

## Revisit when

A row's reason no longer holds: the platform moves, a row's cost shows up in an incident or a
measurement, or the team finds itself working around a row without having recorded why.

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
| Code carries no comment: reasons live in ADRs and pages; only the API's OpenAPI XML documentation, generated headers and one guidance line per setting are kept | A comment rots the next time anything moves | [AGENTS.md](../../AGENTS.md#changing-code) | — |
| .NET SDK pinned by `global.json`, package versions in `Directory.Packages.props`, warnings as errors | One toolchain on every machine and in CI | [Build and CI](../guides/build-and-ci.md#the-gate) | — |
| Every dependency at an exact version: packages, container images by digest, workflow actions by commit; Dependabot proposes updates in bands and ignores none | A tag or a range moves under you; an ignored dependency is one nobody revisits | [Supply chain](../guides/build-and-ci.md#supply-chain) | — |
| ASP.NET Core MVC controllers, `Asp.Versioning`, `Microsoft.AspNetCore.OpenApi`; Scalar in Development | Attribute-routed controllers carry the conventions; the framework's own generator writes the document | [HTTP API](../architecture/http-api.md#versioning-and-the-openapi-document) | — |
| PostgreSQL through EF Core and Npgsql | One relational store with schemas, advisory locks and `xmin` | [Persistence model](../architecture/persistence-model.md) | — |
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
| Migrations applied at release: one bundle per context, built from the image's commit, run by a principal with DDL rights; the API migrates itself in Development only | A process never alters its own schema in production | [Migrations at release](../guides/deployment.md#migrations-at-release) | — |
| The API image on alpine, running unprivileged | The healthcheck needs `wget`, which the chiseled images lack | [The image](../guides/deployment.md#the-image) | — |
| xUnit v3, Shouldly, NSubstitute, Testcontainers, NetArchTest | Integration tests run on a real PostgreSQL; the architecture is held by tests | [Testing](../guides/testing.md) | — |
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

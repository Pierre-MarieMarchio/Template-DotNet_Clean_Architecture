# Clean Architecture .NET Template

A production-shaped starting point for a .NET 10 HTTP API: Clean Architecture layering, PostgreSQL
via EF Core, ASP.NET Identity with JWT access tokens and opaque rotating refresh tokens,
default-deny authorisation, RFC 7807 errors, and configuration that fails fast when it is wrong.

It ships three worked example features — a to-do list with real invariants and domain events, a
flat reminders aggregate, and a file store whose two halves live in different stores — enough to
show an aggregate, a read/write port split and a `Result`-based error policy, without becoming an
application you have to delete. [`docs/REMOVING-THE-EXAMPLE-FEATURES.md`](docs/REMOVING-THE-EXAMPLE-FEATURES.md)
is the procedure for when you have read them.

**New here? [`docs/README.md`](docs/README.md) is the documentation index.** Every document has one
subject, and that page says which one answers the question you actually have.

## What is in the box

- **.NET 10** (`net10.0`), SDK pinned by `global.json`
- **PostgreSQL** through Npgsql — one connection string, one pool, two `DbContext`s (business and
  authentication) with a migration history each, over five schemas
- **ASP.NET Identity** + JWT bearer, opaque refresh tokens that rotate on every use, two-factor
  enrolment, external identity providers, email confirmation by POST, logout that revokes
- **Default-deny authorisation** — a fallback policy requires an authenticated user, so an
  endpoint is protected unless it opts out
- **RFC 7807 ProblemDetails** on every failure, 401 and 403 included, with a stable machine-readable
  `code` and a `traceId`
- **EF Core maps persistence models, not domain entities.** A mapper converts, and a
  reflection-driven fidelity test fails when a property does not survive the round trip
- **Conditional requests** (`ETag`/`If-Match`), **idempotent creates** (`Idempotency-Key`), and a
  closed collection contract with offset *and* keyset paging
- **A second host** — `AppTemplate.Worker` runs the same use cases on timers, behind a PostgreSQL
  advisory-lock lease so it can be replicated
- **Multilingual transactional mail**, subject and body from one template per language
- **Options validated at startup** — a bad setting fails the host, not the first request
- **Central Package Management**, warnings as errors, `.editorconfig` enforced in the build and CI
- **A gate you can run from a clone** — build, format, tests with a coverage floor, migration
  drift, vulnerable packages, and four hygiene gates that each prove they can go red

Two documents are the honest counterweight to that list.
[`SECURITY.md`](SECURITY.md) — whose *what a deployment must still do* section is longer than its
*what the template provides* section — and
[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)'s "What is deliberately absent".

## Prerequisites

| | Needed for |
|---|---|
| [.NET SDK 10.0.300+](https://dotnet.microsoft.com/download) | building and running |
| [Docker](https://docs.docker.com/get-docker/) with Compose v2 | the container path, and the integration tests (Testcontainers) |
| PostgreSQL 17+ | only if you run the API on the host without Docker |

```bash
dotnet --version     # expect 10.0.300 or a later 10.0.3xx
```

`global.json` pins the feature band with `rollForward: latestFeature`, so a later `10.0.3xx` patch
is fine and an `8.x`/`9.x` SDK is refused outright. Nothing else is needed: everything under
`Tools/` is a file-based C# app, so the SDK that builds this repository is also what judges it.

## Five minutes

```bash
git clone <your-fork-url> && cd <the-directory-it-created>

cp .env.example .env          # working localhost defaults
docker compose up --build
```

| | URL |
|---|---|
| API | <http://localhost:8080> |
| OpenAPI UI — Scalar, development only | <http://localhost:8080/scalar/v1> |
| Mailpit — confirmation mails land here | <http://localhost:8025> |
| MinIO console — uploaded files land here | <http://localhost:9001> |

Every published port binds to `127.0.0.1`, so nothing is reachable from your local network. TLS is
not terminated in the container, on purpose.

Then walk an account from registration to a first authenticated request — either with
[`AppTemplate.Api.http`](AppTemplate.Api.http), which is executable in the VS Code REST Client and
in Visual Studio, or with the `curl` transcript in
[`docs/GETTING-STARTED.md`](docs/GETTING-STARTED.md). That document also covers running on the host
without Compose, and supplying your own database.

## Two ways to start

**Clone it.** The repository is a working solution. Read the code, delete what you do not want, and
rename at your leisure.

**Generate from it.** The repository is also a `dotnet new` template, so a project can be minted
under your own name with every namespace, file name and container identifier derived from it:

```bash
dotnet new install <path-to-this-repository>
dotnet new cleanarch-webapi -n Acme.OrderManagement
cd Acme.OrderManagement && dotnet run Tools/Tasks.cs bootstrap
```

That last command is not optional, and
[`docs/USING-THE-TEMPLATE.md`](docs/USING-THE-TEMPLATE.md) says why, along with what the name
substitutes into and what a generated project inherits. If the repository you are generating into
already exists, [`docs/INTEGRATING-INTO-AN-EXISTING-REPOSITORY.md`](docs/INTEGRATING-INTO-AN-EXISTING-REPOSITORY.md)
measures the two shapes that work.

## Documentation

[`docs/README.md`](docs/README.md) is the index. The six documents most people want first:

| Document | For |
|---|---|
| [`docs/GETTING-STARTED.md`](docs/GETTING-STARTED.md) | getting it running, and the walkthrough from register to first request |
| [`docs/API-CONVENTIONS.md`](docs/API-CONVENTIONS.md) | what holds for every endpoint — error codes, `ETag`, idempotency, paging |
| [`docs/API-REFERENCE.md`](docs/API-REFERENCE.md) | the endpoints, one table per feature |
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | the layers, the dependency rule, and what is deliberately absent |
| [`docs/ADDING-A-FEATURE.md`](docs/ADDING-A-FEATURE.md) | the vertical, end to end, with the real signatures |
| [`docs/CONFIGURATION.md`](docs/CONFIGURATION.md) | every key, its default, and what happens when it is wrong |

[`CONTRIBUTING.md`](CONTRIBUTING.md) is the working agreement for changing this repository.

## The tree

```
Src/
  Domain/            AppTemplate.Domain.Core, AppTemplate.Domain
  Application/       AppTemplate.Application.Core, AppTemplate.Application,
                     AppTemplate.Application.Auth
  Infrastructure/    AppTemplate.Infrastructure.Core, .Persistence, .Auth,
                     .Email, .Storage, .InMemory
  Presentation/      AppTemplate.Presentation.Core, AppTemplate.Api.Core,
                     AppTemplate.Api, AppTemplate.Worker
Tests/               a 1:1 mirror of Src/, plus Architecture/ and Integration/
Tools/               the file-based C# apps this repository runs on itself
docs/                the documentation index and everything it lists
deploy/kubernetes/   raw manifests, no Helm chart
```

The directory under `Src/` names the **layer**; the project inside keeps its own name and root
namespace, so moving a project between layer folders changes no namespace. Six of those projects
are written to package grade — tracked public surface, `dotnet pack` over them — and vendored
rather than published. [`docs/PROJECT-LAYOUT.md`](docs/PROJECT-LAYOUT.md) is the full map, down to
what each folder holds and which architecture test enforces it.

## Licence

MIT — see [`LICENSE`](LICENSE).

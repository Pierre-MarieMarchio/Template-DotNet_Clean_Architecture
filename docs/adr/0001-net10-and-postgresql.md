# 0001 — .NET 10 and PostgreSQL

Status: Accepted

## Context

The template needs one runtime and one relational database, chosen once and pinned, so
that "it works on my machine" is a statement about a version and not a guess. What it
inherited was neither: package versions drifted across the `8.0.x` and `9.0.x` bands in
different projects, and the database was SQL Server via a `mcr.microsoft.com/mssql`
container of about 1.5 GB, carrying an EULA, an `SA` account, and — in the compose file
as shipped — an empty `SA_PASSWORD` that made the service refuse to start.

## Decision

**.NET 10 (`net10.0`)**, with the SDK feature band pinned in `global.json`
(`10.0.300`, `rollForward: latestFeature`), and every package version declared exactly
once in `Directory.Packages.props` under Central Package Management. All `Microsoft.*`
runtime packages stay on the same `10.0.x` band.

**PostgreSQL**, through `Npgsql.EntityFrameworkCore.PostgreSQL`, pinned in
`docker-compose.yml` to `postgres:18.4-alpine3.23` — a patch version, including the
Alpine base, because an unattended minor upgrade of a stateful service is how data
directories get orphaned.

## Consequences

- A later `10.0.3xx` SDK patch is accepted; an `8.x` or `9.x` SDK is refused outright
  rather than producing a build that differs from CI's.
- Version drift becomes a restore error instead of a runtime surprise. `NuGetAudit`
  with `NuGetAuditMode=all` and `NuGetAuditLevel=low` then makes any known advisory a
  build failure — which is why `Microsoft.OpenApi` needs an explicit transitive pin
  (see [README](../../README.md#supply-chain)).
- PostgreSQL-specific features are used where they are the right answer, and the
  template is honest about it rather than pretending to be engine-agnostic:
  `TodoList.Version` maps to the `xmin` system column, and schemas separate the two
  DbContexts. A `byte[]` rowversion is a SQL Server feature PostgreSQL would only
  emulate with a trigger.
- The local stack is ~110 MB instead of ~1.5 GB, with no EULA and no `SA` account, and
  `POSTGRES_INITDB_ARGS: --encoding=UTF8 --locale=C` makes collation deterministic
  rather than dependent on the host.
- Moving to another engine means revisiting the concurrency token, the schema
  separation and `EnableRetryOnFailure`. That is a real cost, accepted knowingly.

## Alternatives rejected

- **Stay on .NET 8 LTS.** Defensible for an application with a support contract, wrong
  for a template: a template's job is to show the current shape, and starting a new
  project on the previous major means the first task is an upgrade.
- **Keep SQL Server.** Heavier image, licensing to think about, and a privileged `SA`
  account in a development stack. Nothing in the sample needed a SQL Server feature.
- **SQLite.** Excellent for tests, wrong as the reference engine: it has no schemas, no
  real concurrency story, and a template that develops against SQLite and deploys
  against something else teaches the wrong habits.
- **Float package versions (`10.0.*`).** Reproducibility is the whole point of pinning.
  A floating range makes "which version has the CVE" unanswerable from the repository.

## Revisit when

.NET 12 ships and .NET 10's support window shortens; or a deployment target genuinely
mandates a different engine, at which point the concurrency token and schema decisions
must be reopened together, not one at a time.
